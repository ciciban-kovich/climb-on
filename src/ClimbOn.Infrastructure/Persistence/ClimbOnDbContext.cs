using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClimbOn.Infrastructure.Persistence;

public sealed class ClimbOnDbContext(DbContextOptions<ClimbOnDbContext> options) : DbContext(options)
{
    public const string ConnectionStringName = "ClimbOn";

    public DbSet<Postcode> Postcodes => Set<Postcode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Postcode>(postcode =>
        {
            postcode.ToTable("postcodes", table =>
            {
                table.HasCheckConstraint("ck_postcodes_code", "code ~ '^[0-9]{4}$'");
                table.HasCheckConstraint("ck_postcodes_lat", "lat BETWEEN -90 AND 90");
                table.HasCheckConstraint("ck_postcodes_lon", "lon BETWEEN -180 AND 180");
            });
            postcode.HasKey(row => row.Code).HasName("pk_postcodes");
            postcode.Property(row => row.Code).HasColumnName("code").HasMaxLength(4);
            postcode.Property(row => row.Latitude).HasColumnName("lat");
            postcode.Property(row => row.Longitude).HasColumnName("lon");
            postcode.Property(row => row.Retired).HasColumnName("retired");
        });
    }

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
