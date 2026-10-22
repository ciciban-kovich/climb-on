using ClimbOn.Infrastructure.Persistence.Accounts;
using ClimbOn.Infrastructure.Persistence.Outbox;
using ClimbOn.Infrastructure.Persistence.Sessions;
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

    public DbSet<LinkTokenRow> LinkTokens => Set<LinkTokenRow>();

    public DbSet<OutboxRow> Outbox => Set<OutboxRow>();

    public DbSet<SessionRow> Sessions => Set<SessionRow>();

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
            account.Property(row => row.EmailConfirmedAt).HasColumnName("email_confirmed_at");
            account.Property(row => row.LastActivityAt).HasColumnName("last_activity_at");
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

        modelBuilder.Entity<LinkTokenRow>(token =>
        {
            token.ToTable("link_tokens", table =>
            {
                table.HasCheckConstraint("ck_link_tokens_purpose", $"purpose IN ('{LinkPurposes.EmailConfirmation}')");
                table.HasCheckConstraint("ck_link_tokens_token_hash", "octet_length(token_hash) = 32");
            });
            token.HasKey(row => row.TokenHash).HasName("pk_link_tokens");
            token.Property(row => row.TokenHash).HasColumnName("token_hash");
            token.Property(row => row.AccountId).HasColumnName("account_id");
            token.Property(row => row.Purpose).HasColumnName("purpose").HasMaxLength(32);
            token.Property(row => row.CreatedAt).HasColumnName("created_at");
            token.Property(row => row.UsedAt).HasColumnName("used_at");
            token.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.AccountId)
                .HasConstraintName("fk_link_tokens_accounts").OnDelete(DeleteBehavior.Cascade);
            token.HasIndex(row => row.AccountId).HasDatabaseName("ix_link_tokens_account_id");
        });

        modelBuilder.Entity<OutboxRow>(outbox =>
        {
            outbox.ToTable("outbox", table =>
                table.HasCheckConstraint("ck_outbox_attempts", "attempts >= 0"));
            outbox.HasKey(row => row.Id).HasName("pk_outbox");
            outbox.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            outbox.Property(row => row.AccountId).HasColumnName("account_id");
            outbox.Property(row => row.Recipient).HasColumnName("recipient").HasMaxLength(254);
            outbox.Property(row => row.Subject).HasColumnName("subject");
            outbox.Property(row => row.Body).HasColumnName("body");
            outbox.Property(row => row.CreatedAt).HasColumnName("created_at");
            outbox.Property(row => row.Attempts).HasColumnName("attempts");
            outbox.Property(row => row.NextAttemptAt).HasColumnName("next_attempt_at");
            outbox.Property(row => row.SentAt).HasColumnName("sent_at");
            outbox.Property(row => row.FailedAt).HasColumnName("failed_at");
            outbox.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.AccountId)
                .HasConstraintName("fk_outbox_accounts").OnDelete(DeleteBehavior.Cascade);
            outbox.HasIndex(row => row.AccountId).HasDatabaseName("ix_outbox_account_id");
            outbox.HasIndex(row => row.NextAttemptAt).HasDatabaseName("ix_outbox_due")
                .HasFilter("sent_at IS NULL AND failed_at IS NULL");
        });

        modelBuilder.Entity<SessionRow>(session =>
        {
            session.ToTable("sessions", table =>
                table.HasCheckConstraint("ck_sessions_token_hash", "octet_length(token_hash) = 32"));
            session.HasKey(row => row.TokenHash).HasName("pk_sessions");
            session.Property(row => row.TokenHash).HasColumnName("token_hash");
            session.Property(row => row.AccountId).HasColumnName("account_id");
            session.Property(row => row.CreatedAt).HasColumnName("created_at");
            session.Property(row => row.LastUsedAt).HasColumnName("last_used_at");
            session.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.AccountId)
                .HasConstraintName("fk_sessions_accounts").OnDelete(DeleteBehavior.Cascade);
            session.HasIndex(row => row.AccountId).HasDatabaseName("ix_sessions_account_id");
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
