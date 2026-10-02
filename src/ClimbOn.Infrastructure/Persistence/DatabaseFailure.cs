using Npgsql;

namespace ClimbOn.Infrastructure.Persistence;

public static class DatabaseFailure
{
    // Server states that mean "cannot serve connections now", not "this statement failed".
    private static readonly HashSet<string> UnavailableStates = ["57P01", "57P02", "57P03", "53300"];

    // True when the exception, or one it wraps, says the database could not be reached or used
    // at all: refused or broken connections, timeouts, shutdowns. A failing statement is not one.
    public static bool IsUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException postgres:
                    return postgres.SqlState.StartsWith("08", StringComparison.Ordinal)
                        || UnavailableStates.Contains(postgres.SqlState);
                case NpgsqlException:
                    return true;
            }
        }

        return false;
    }
}
