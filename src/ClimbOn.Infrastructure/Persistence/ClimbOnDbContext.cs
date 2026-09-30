using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClimbOn.Infrastructure.Persistence;

public sealed class ClimbOnDbContext(DbContextOptions<ClimbOnDbContext> options) : DbContext(options)
{
    public const string ConnectionStringName = "ClimbOn";

    // C3: every timestamp is stored as timestamptz; Npgsql rejects DateTime values whose Kind is not Utc.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveColumnType("timestamp with time zone");
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamp with time zone");
    }
}

// Used only by `dotnet ef` to build the model; it never opens a connection.
internal sealed class ClimbOnDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ClimbOnDbContext>
{
    public ClimbOnDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ClimbOnDbContext>().UseNpgsql("Host=localhost;Database=climbon").Options);
}
