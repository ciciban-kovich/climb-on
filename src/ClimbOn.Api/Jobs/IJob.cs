namespace ClimbOn.Api.Jobs;

// A job entrypoint. Jobs are registered in DI and resolved from a fresh scope per run.
public interface IJob
{
    string Name { get; }

    // Returns the process exit code: 0 on success.
    Task<int> RunAsync(CancellationToken cancellationToken);
}
