namespace ClimbOn.Api.Jobs;

// The one dispatch path for jobs, shared by the container entrypoint and the integration tests (design §5).
public static class JobRunner
{
    public static async Task<int> RunAsync(IServiceProvider services, string name, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetServices<IJob>().SingleOrDefault(job => job.Name == name)
            ?? throw new InvalidOperationException($"Unknown job '{name}'.");
        return await job.RunAsync(cancellationToken);
    }
}
