using ClimbOn.Application.Postcodes;

namespace ClimbOn.Api.Jobs;

// S101: the monthly postcode import. A failure propagates, so the job fails and the table is unchanged (S101.2).
public sealed partial class PostcodesJob(PostcodeImport import, ILogger<PostcodesJob> logger) : IJob
{
    public const string JobName = "postcodes";

    public string Name => JobName;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var result = await import.RunAsync(cancellationToken);
        LogCompleted(result.Added, result.Changed, result.Retired, result.Reactivated, result.ActiveTotal);
        return 0;
    }

    // Counts only (C32). Task 31's staging check reads this event.
    [LoggerMessage(
        EventName = "PostcodeImportCompleted",
        Level = LogLevel.Information,
        Message = "Postcode import completed: {Added} added, {Changed} changed, {Retired} retired, {Reactivated} reactivated, {ActiveTotal} active")]
    private partial void LogCompleted(int added, int changed, int retired, int reactivated, int activeTotal);
}
