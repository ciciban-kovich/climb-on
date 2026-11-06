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

    private async Task<long> SessionsAsync(InviteClimber climber)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM sessions WHERE account_id = @id", connection);
        command.Parameters.AddWithValue("id", climber.Id);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }
}
