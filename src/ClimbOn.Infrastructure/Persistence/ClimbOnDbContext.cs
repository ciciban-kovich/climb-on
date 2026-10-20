using ClimbOn.Infrastructure.Persistence.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClimbOn.Infrastructure.Persistence;

public sealed class ClimbOnDbContext(DbContextOptions<ClimbOnDbContext> options) : DbContext(options)
{
    public const string ConnectionStringName = "ClimbOn";

    public DbSet<Postcode> Postcodes => Set<Postcode>();

    public DbSet<AccountRow> Accounts => Set<AccountRow>();

    public DbSet<LegalDocumentRow> LegalDocuments => Set<LegalDocumentRow>();

    public DbSet<LegalAcknowledgementRow> LegalAcknowledgements => Set<LegalAcknowledgementRow>();

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

        modelBuilder.Entity<AccountRow>(account =>
        {
            account.ToTable("accounts", table =>
                table.HasCheckConstraint("ck_accounts_language", "language IN ('en', 'nb')"));
            account.HasKey(row => row.Id).HasName("pk_accounts");
            account.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            account.Property(row => row.Email).HasColumnName("email").HasMaxLength(254);
            account.Property(row => row.PasswordHash).HasColumnName("password_hash");
            account.Property(row => row.DateOfBirth).HasColumnName("date_of_birth");
            account.Property(row => row.Language).HasColumnName("language").HasMaxLength(2);
            account.Property(row => row.RegisteredAt).HasColumnName("registered_at");
        });

        modelBuilder.Entity<LegalDocumentRow>(document =>
        {
            document.ToTable("legal_documents", table =>
            {
                table.HasCheckConstraint("ck_legal_documents_document", "document IN ('terms', 'privacy')");
                table.HasCheckConstraint("ck_legal_documents_version", "version >= 1");
            });
            document.HasKey(row => new { row.Document, row.Version }).HasName("pk_legal_documents");
            document.Property(row => row.Document).HasColumnName("document").HasMaxLength(16);
            document.Property(row => row.Version).HasColumnName("version").ValueGeneratedNever();
            document.Property(row => row.PublishedAt).HasColumnName("published_at");
            document.Property(row => row.TextEnglish).HasColumnName("text_en");
            document.Property(row => row.TextBokmal).HasColumnName("text_nb");
            document.HasData(LegalDocumentSeed.Rows);
        });

        modelBuilder.Entity<LegalAcknowledgementRow>(acknowledgement =>
        {
            acknowledgement.ToTable("legal_acknowledgements");
            acknowledgement.HasKey(row => new { row.AccountId, row.Document, row.Version }).HasName("pk_legal_acknowledgements");
            acknowledgement.Property(row => row.AccountId).HasColumnName("account_id");
            acknowledgement.Property(row => row.Document).HasColumnName("document").HasMaxLength(16);
            acknowledgement.Property(row => row.Version).HasColumnName("version");
            acknowledgement.Property(row => row.AcknowledgedAt).HasColumnName("acknowledged_at");
            acknowledgement.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.AccountId)
                .HasConstraintName("fk_legal_acknowledgements_accounts").OnDelete(DeleteBehavior.Cascade);
            acknowledgement.HasOne<LegalDocumentRow>().WithMany().HasForeignKey(row => new { row.Document, row.Version })
                .HasConstraintName("fk_legal_acknowledgements_legal_documents").OnDelete(DeleteBehavior.Restrict);
            acknowledgement.HasIndex(row => new { row.Document, row.Version }).HasDatabaseName("ix_legal_acknowledgements_document_version");
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
