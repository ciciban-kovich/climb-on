using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ClimbOn.Infrastructure.Persistence.Jobs;

// Design §4.1 'Single sweeper': a job's session-level advisory lock, one per job name in each database. It is a
// one-key lock, a key space apart from the two-key pair locks (PairLocks).
public sealed class JobLocks(ClimbOnDbContext db)
{
    // The lock for job, held until the result is disposed; null when another session holds it, without waiting. It is
    // held by a connection of its own outside the pool, so that disposing it ends the session and with it the lock,
    // also when the connection has failed: a pooled connection would keep its session, lock included, in the pool.
    public async Task<IAsyncDisposable?> TryAcquireAsync(string job, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Pooling = false }.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtext(@job))", connection);
            command.Parameters.AddWithValue("job", job);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                return connection;
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }
}
