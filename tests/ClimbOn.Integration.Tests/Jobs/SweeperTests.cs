using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Jobs;
using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Notifications;
using ClimbOn.Application.Outbox;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Invites;
using ClimbOn.Infrastructure.Persistence.Jobs;
using ClimbOn.Integration.Tests.Harness;
using ClimbOn.Integration.Tests.Invites;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ClimbOn.Integration.Tests.Jobs;

// The sweeper job (design §4.1, §4.2), run in-process after the clock is moved. What it materialises changes no API
// answer, since reads already compute those states from the clock, so these tests read the stored rows below the API.
public sealed class SweeperTests(ApiFactory factory) : IntegrationTest(factory)
{
    private readonly InviteClimbers climbers = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // S8: an invite whose 7 days are over is written expired by timeout, ended when its time ran out; one still inside
    // its 7 days stays pending.
    [Fact]
    public async Task Materialises_expired_invites()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var c = await climbers.ClimberAsync();
        var due = await climbers.InvitedAsync(a, b);
        Factory.Clock.Advance(TimeSpan.FromDays(1));
        var notDue = await climbers.InvitedAsync(a, c);
        var sentAt = FixedClock.Start;
        Factory.Clock.Set(sentAt + Invite.Lifetime);

        Assert.Equal(0, await Factory.RunJobAsync(SweeperJob.JobName));

        Assert.Equal(("expired", "timeout", (DateTimeOffset?)(sentAt + Invite.Lifetime)), await StoredInviteAsync(due.InviteId!.Value));
        Assert.Equal(("pending", null, null), await StoredInviteAsync(notDue.InviteId!.Value));

        // A second run finds nothing left to do.
        Assert.Equal(0, await Factory.RunJobAsync(SweeperJob.JobName));
        Assert.Equal(("expired", "timeout", (DateTimeOffset?)(sentAt + Invite.Lifetime)), await StoredInviteAsync(due.InviteId!.Value));
    }

    // S8, S33: a timeout the sweeper writes after the 7 days are over still ends the invite when its time ran out, so
    // the 30-day bar runs from then, not from the sweep.
    [Fact]
    public async Task Late_timeout_ends_when_its_time_ran_out()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var sentAt = Factory.Clock.UtcNow;
        var invite = await climbers.InvitedAsync(a, b);
        Factory.Clock.Set(sentAt + Invite.Lifetime + TimeSpan.FromHours(5));

        Assert.Equal(0, await Factory.RunJobAsync(SweeperJob.JobName));

        Assert.Equal(("expired", "timeout", (DateTimeOffset?)(sentAt + Invite.Lifetime)), await StoredInviteAsync(invite.InviteId!.Value));
    }

    // Design §4.2: the sweeper writes a timeout under its pair's lock, so it waits while a pair action holds that lock
    // and writes the timeout once the action has committed.
    [Fact]
    public async Task Timeout_waits_for_the_pair_lock()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = await climbers.InvitedAsync(a, b);
        Factory.Clock.Advance(Invite.Lifetime);
        await using var holder = new NpgsqlConnection(Factory.ConnectionString);
        await holder.OpenAsync(Ct);
        await using var transaction = await holder.BeginTransactionAsync(Ct);
        await LockPairAsync(holder, a.Id, b.Id);

        var sweep = Task.Run(() => Factory.RunJobAsync(SweeperJob.JobName), Ct);
        using var waitLimit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        waitLimit.CancelAfter(TimeSpan.FromSeconds(10));
        while (!sweep.IsCompleted && !await PairLockWaitedForAsync(holder))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), waitLimit.Token);
        }

        Assert.False(sweep.IsCompleted, "The sweeper wrote the timeout without waiting for the pair lock.");
        Assert.Equal("pending", (await StoredInviteAsync(invite.InviteId!.Value)).State);

        await transaction.CommitAsync(Ct);
        Assert.Equal(0, await sweep);
        Assert.Equal("expired", (await StoredInviteAsync(invite.InviteId!.Value)).State);
    }

    // Design §4.1: a run that finds another run holding the lock exits at once, doing nothing; once the lock is free,
    // the next run does the work.
    [Fact]
    public async Task Concurrent_run_exits_without_work()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = await climbers.InvitedAsync(a, b);
        Factory.Clock.Advance(Invite.Lifetime);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await using var held = await scope.ServiceProvider.GetRequiredService<JobLocks>().TryAcquireAsync(SweeperJob.JobName, Ct);
            Assert.NotNull(held);

            Assert.Equal(0, await Factory.RunJobAsync(SweeperJob.JobName));
            Assert.Equal("pending", (await StoredInviteAsync(invite.InviteId!.Value)).State);
        }

        Assert.Equal(0, await Factory.RunJobAsync(SweeperJob.JobName));
        Assert.Equal("expired", (await StoredInviteAsync(invite.InviteId!.Value)).State);
    }

    // Design §2 'Maintenance mode': the job exits at start and writes nothing. The host is one of the test's own, with
    // maintenance on, on this test's database.
    [Fact]
    [Trait("Compat", "exclude")]
    public async Task Maintenance_mode_exits()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = await climbers.InvitedAsync(a, b);
        Factory.Clock.Advance(Invite.Lifetime);
        await using var maintenance = ApiFactory.For(
            new TestDatabaseSettings(Factory.ConnectionString, SkipMigrations: true),
            new Dictionary<string, string?> { [SweeperJob.MaintenanceKey] = "true" });
        await maintenance.InitializeAsync();
        maintenance.Clock.Set(Factory.Clock.UtcNow);

        Assert.Equal(0, await maintenance.RunJobAsync(SweeperJob.JobName));

        Assert.Equal("pending", (await StoredInviteAsync(invite.InviteId!.Value)).State);
        Assert.Equal(0, await Factory.RunJobAsync(SweeperJob.JobName));
        Assert.Equal("expired", (await StoredInviteAsync(invite.InviteId!.Value)).State);
    }

    // Design §4.1: rows whose delivery after commit did not happen are delivered by the sweeper, a notification event
    // into the in-app list and an email to the sender. They are written below the API without being delivered, as a
    // request that failed after its commit leaves them.
    [Fact]
    public async Task Retries_undelivered_outbox_rows()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        Factory.Email.Clear();
        var email = new EmailMessage(b.Account.Email, "Subject", "Body");
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<OutboxQueue>();
            outbox.Enqueue(new NotificationEvent(NotificationKind.InviteReceived, b.Id, a.Id, Guid.NewGuid()));
            await outbox.EnqueueAsync(b.Id, email, Ct);
        }

        Assert.Empty(await NotificationsAsync(b));
        Assert.Empty(Factory.Email.Sent);
        var queuedAt = Factory.Clock.UtcNow;
        Factory.Clock.Advance(TimeSpan.FromMinutes(15));

        Assert.Equal(0, await Factory.RunJobAsync(SweeperJob.JobName));

        var notification = Assert.Single(await NotificationsAsync(b));
        Assert.Equal("invite-received", notification.Event);
        Assert.Equal(a.Name, notification.DisplayName);
        Assert.Equal(queuedAt, notification.CreatedAt);
        Assert.Equal(email, Assert.Single(Factory.Email.Sent));

        // Each was delivered once.
        Assert.Equal(0, await Factory.RunJobAsync(SweeperJob.JobName));
        Assert.Single(await NotificationsAsync(b));
        Assert.Single(Factory.Email.Sent);
    }

    // S82: a session expires 30 days after its last use; the sweeper deletes the expired ones and keeps the live.
    [Fact]
    public async Task Purges_expired_sessions()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        Factory.Clock.Advance(TimeSpan.FromDays(1));
        using (var response = await climbers.GetListAsync(b))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Factory.Clock.Set(FixedClock.Start + SessionPolicy.Lifetime);

        Assert.Equal(0, await Factory.RunJobAsync(SweeperJob.JobName));

        Assert.Equal(0, await SessionsAsync(a));
        Assert.Equal(1, await SessionsAsync(b));
        using (var response = await climbers.GetListAsync(b))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private async Task<IReadOnlyList<NotificationResponse>> NotificationsAsync(InviteClimber climber)
    {
        using var response = await climbers.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/climber/notifications"), climber.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NotificationListResponse>(Ct))!.Notifications;
    }

    private async Task<(string State, string? Cause, DateTimeOffset? EndedAt)> StoredInviteAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT state, expiry_cause, ended_at FROM invites WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        return (
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    // The pair's advisory lock as a pair action takes it (design §2), in the holder's open transaction. The key is
    // written out here as PairLocks derives it; if the two drift apart, Timeout_waits_for_the_pair_lock fails.
    private static async Task LockPairAsync(NpgsqlConnection holder, Guid a, Guid b)
    {
        var (low, high) = string.CompareOrdinal(a.ToString("D"), b.ToString("D")) < 0 ? (a, b) : (b, a);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(19, hashtext(@low || ':' || @high))", holder);
        command.Parameters.AddWithValue("low", low.ToString("D"));
        command.Parameters.AddWithValue("high", high.ToString("D"));
        await command.ExecuteNonQueryAsync(Ct);
    }

    // Whether another session of this database is waiting for a two-key advisory lock in the pair locks' key space.
    private static async Task<bool> PairLockWaitedForAsync(NpgsqlConnection holder)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'advisory' AND NOT granted AND classid = 19 AND objsubid = 2
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """,
            holder);
        return (long)(await command.ExecuteScalarAsync(Ct))! > 0;
    }

    private async Task<long> SessionsAsync(InviteClimber climber)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM sessions WHERE account_id = @id", connection);
        command.Parameters.AddWithValue("id", climber.Id);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }
}
