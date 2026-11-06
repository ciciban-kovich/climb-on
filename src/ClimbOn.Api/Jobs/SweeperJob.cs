using ClimbOn.Application.Accounts;
using ClimbOn.Application.Invites;
using ClimbOn.Application.Outbox;
using ClimbOn.Infrastructure.Persistence.Jobs;

namespace ClimbOn.Api.Jobs;

// Design §4.2: every 15 minutes, materialises the time-derived states that are due and retries the outbox. It exits
// at start under maintenance mode (design §2) and when another run holds its lock (design §4.1 'Single sweeper').
// Every step is idempotent, so a run killed midway is completed by the next.
public sealed partial class SweeperJob(
    IConfiguration configuration,
    JobLocks locks,
    ExpireInvites invites,
    Sessions sessions,
    OutboxQueue outbox,
    ILogger<SweeperJob> logger) : IJob
{
    public const string JobName = "sweeper";

    public const string MaintenanceKey = "Maintenance:Enabled";

    public string Name => JobName;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (configuration.GetValue<bool>(MaintenanceKey))
        {
            LogMaintenance();
            return 0;
        }

        await using var held = await locks.TryAcquireAsync(JobName, cancellationToken);
        if (held is null)
        {
            LogLockHeld();
            return 0;
        }

        var expired = await invites.RunAsync(cancellationToken);
        var purged = await sessions.PurgeExpiredAsync(cancellationToken);
        await outbox.RetryDueAsync(cancellationToken);
        LogCompleted(expired, purged);
        return 0;
    }

    [LoggerMessage(EventName = "SweeperMaintenance", Level = LogLevel.Information, Message = "Sweeper skipped: maintenance mode")]
    private partial void LogMaintenance();

    [LoggerMessage(EventName = "SweeperLockHeld", Level = LogLevel.Information, Message = "Sweeper skipped: another run holds the lock")]
    private partial void LogLockHeld();

    // Counts only (C32).
    [LoggerMessage(
        EventName = "SweeperCompleted",
        Level = LogLevel.Information,
        Message = "Sweeper completed: {ExpiredInvites} invites expired, {PurgedSessions} sessions purged")]
    private partial void LogCompleted(int expiredInvites, int purgedSessions);
}
