using ClimbOn.Infrastructure.Persistence.Accounts;
using ClimbOn.Infrastructure.Persistence.Chat;
using ClimbOn.Infrastructure.Persistence.Invites;
using ClimbOn.Infrastructure.Persistence.Notifications;
using ClimbOn.Infrastructure.Persistence.Outbox;
using ClimbOn.Infrastructure.Persistence.Profiles;
using ClimbOn.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClimbOn.Infrastructure.Persistence;

public sealed class ClimbOnDbContext(DbContextOptions<ClimbOnDbContext> options) : DbContext(options)
{
    public const string ConnectionStringName = "ClimbOn";

    // S84: one account per normalised address.
    public const string AccountEmailIndex = "ix_accounts_email_normalised";

    // S32: one pending invite per sender and recipient.
    public const string PendingInviteIndex = "ix_invites_pending_pair";

    // One active or paused connection per pair (design §2 'Concurrency for pair and count rules').
    public const string LiveConnectionIndex = "ix_connections_live_pair";

    public DbSet<Postcode> Postcodes => Set<Postcode>();

    public DbSet<AccountRow> Accounts => Set<AccountRow>();

    public DbSet<LegalDocumentRow> LegalDocuments => Set<LegalDocumentRow>();

    public DbSet<LegalAcknowledgementRow> LegalAcknowledgements => Set<LegalAcknowledgementRow>();

    public DbSet<LinkTokenRow> LinkTokens => Set<LinkTokenRow>();

    public DbSet<OutboxRow> Outbox => Set<OutboxRow>();

    public DbSet<SessionRow> Sessions => Set<SessionRow>();

    public DbSet<GymRow> Gyms => Set<GymRow>();

    public DbSet<ProfileRow> Profiles => Set<ProfileRow>();

    public DbSet<DisciplineVariantRow> DisciplineVariants => Set<DisciplineVariantRow>();

    public DbSet<AvailabilityWindowRow> AvailabilityWindows => Set<AvailabilityWindowRow>();

    public DbSet<HomeGymRow> HomeGyms => Set<HomeGymRow>();

    public DbSet<InviteRow> Invites => Set<InviteRow>();

    public DbSet<ConnectionRow> Connections => Set<ConnectionRow>();

    public DbSet<SafetyAcknowledgementRow> SafetyAcknowledgements => Set<SafetyAcknowledgementRow>();

    public DbSet<MessageRow> Messages => Set<MessageRow>();

    public DbSet<NotificationRow> Notifications => Set<NotificationRow>();

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
            account.Property(row => row.NormalisedEmail).HasColumnName("email_normalised").HasMaxLength(254);
            account.HasIndex(row => row.NormalisedEmail).IsUnique().HasDatabaseName(AccountEmailIndex);
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
            {
                table.HasCheckConstraint("ck_outbox_attempts", "attempts >= 0");
                table.HasCheckConstraint("ck_outbox_kind", $"kind IN ('{OutboxRow.EmailKind}', '{OutboxRow.NotificationKind}')");
                table.HasCheckConstraint(
                    "ck_outbox_notification",
                    $"(kind = '{OutboxRow.NotificationKind}') = (event IS NOT NULL AND actor_id IS NOT NULL AND subject_id IS NOT NULL)");
                table.HasCheckConstraint("ck_outbox_event", NotificationEventCheck);
            });
            outbox.HasKey(row => row.Id).HasName("pk_outbox");
            outbox.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            outbox.Property(row => row.AccountId).HasColumnName("account_id");
            // C29: the previous app version writes email rows without a kind.
            outbox.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(16).HasDefaultValue(OutboxRow.EmailKind);
            outbox.Property(row => row.Event).HasColumnName("event").HasMaxLength(32);
            outbox.Property(row => row.ActorId).HasColumnName("actor_id");
            outbox.Property(row => row.SubjectId).HasColumnName("subject_id");
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
            outbox.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.ActorId)
                .HasConstraintName("fk_outbox_accounts_actor").OnDelete(DeleteBehavior.Cascade);
            outbox.HasIndex(row => row.ActorId).HasDatabaseName("ix_outbox_actor_id");
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

        modelBuilder.Entity<GymRow>(gym =>
        {
            gym.ToTable("gyms", table =>
                table.HasCheckConstraint("ck_gyms_postcode", "postcode ~ '^[0-9]{4}$'"));
            gym.HasKey(row => row.Id).HasName("pk_gyms");
            gym.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            gym.Property(row => row.Name).HasColumnName("name");
            gym.Property(row => row.Address).HasColumnName("address");
            gym.Property(row => row.Postcode).HasColumnName("postcode").HasMaxLength(4);
            gym.Property(row => row.Active).HasColumnName("active");
        });

        modelBuilder.Entity<ProfileRow>(profile =>
        {
            profile.ToTable("profiles", table =>
            {
                table.HasCheckConstraint("ck_profiles_sex", "sex IN ('female', 'male', 'other')");
                table.HasCheckConstraint(
                    "ck_profiles_accepted_sexes",
                    "cardinality(accepted_sexes) >= 1 AND accepted_sexes <@ ARRAY['female', 'male', 'other']::text[]");
                table.HasCheckConstraint("ck_profiles_rope_scale", "rope_scale IN ('norwegian', 'french')");
                table.HasCheckConstraint(
                    "ck_profiles_certification", "certification IN ('none', 'brattkort-topptau', 'brattkort-led')");
                table.HasCheckConstraint("ck_profiles_postcode", "postcode ~ '^[0-9]{4}$'");
                table.HasCheckConstraint("ck_profiles_max_travel_km", "max_travel_km BETWEEN 1 AND 100");
            });
            profile.HasKey(row => row.AccountId).HasName("pk_profiles");
            profile.Property(row => row.AccountId).HasColumnName("account_id");
            profile.Property(row => row.DisplayName).HasColumnName("display_name");
            profile.Property(row => row.Sex).HasColumnName("sex").HasMaxLength(8);
            profile.Property(row => row.AcceptedSexes).HasColumnName("accepted_sexes");
            profile.Property(row => row.RopeScale).HasColumnName("rope_scale").HasMaxLength(16);
            profile.Property(row => row.Certification).HasColumnName("certification").HasMaxLength(32);
            profile.Property(row => row.OwnGear).HasColumnName("own_gear");
            profile.Property(row => row.Postcode).HasColumnName("postcode").HasMaxLength(4);
            profile.Property(row => row.MaxTravelKm).HasColumnName("max_travel_km");
            profile.HasIndex(row => row.Postcode).HasDatabaseName("ix_profiles_postcode");
            profile.HasOne<AccountRow>().WithOne().HasForeignKey<ProfileRow>(row => row.AccountId)
                .HasConstraintName("fk_profiles_accounts").OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DisciplineVariantRow>(variant =>
        {
            variant.ToTable("discipline_variants", table =>
            {
                table.HasCheckConstraint(
                    "ck_discipline_variants_variant",
                    "variant IN ('bouldering-indoor', 'bouldering-outdoor', 'top-rope-indoor', 'top-rope-outdoor', "
                    + "'lead-indoor', 'lead-outdoor', 'multipitch-outdoor', 'ice-outdoor')");
                table.HasCheckConstraint("ck_discipline_variants_scale", "scale IN ('norwegian', 'french', 'font', 'water-ice')");
                table.HasCheckConstraint("ck_discipline_variants_own", "own_lowest_position <= own_highest_position");
                table.HasCheckConstraint("ck_discipline_variants_partner", "partner_lowest_position <= partner_highest_position");
            });
            variant.HasKey(row => new { row.AccountId, row.Variant }).HasName("pk_discipline_variants");
            variant.Property(row => row.AccountId).HasColumnName("account_id");
            variant.Property(row => row.Variant).HasColumnName("variant").HasMaxLength(32);
            variant.Property(row => row.Shared).HasColumnName("shared");
            variant.Property(row => row.Scale).HasColumnName("scale").HasMaxLength(16);
            variant.Property(row => row.OwnLowest).HasColumnName("own_lowest").HasMaxLength(8);
            variant.Property(row => row.OwnLowestPosition).HasColumnName("own_lowest_position");
            variant.Property(row => row.OwnHighest).HasColumnName("own_highest").HasMaxLength(8);
            variant.Property(row => row.OwnHighestPosition).HasColumnName("own_highest_position");
            variant.Property(row => row.PartnerLowest).HasColumnName("partner_lowest").HasMaxLength(8);
            variant.Property(row => row.PartnerLowestPosition).HasColumnName("partner_lowest_position");
            variant.Property(row => row.PartnerHighest).HasColumnName("partner_highest").HasMaxLength(8);
            variant.Property(row => row.PartnerHighestPosition).HasColumnName("partner_highest_position");
            variant.HasOne<ProfileRow>().WithMany().HasForeignKey(row => row.AccountId)
                .HasConstraintName("fk_discipline_variants_profiles").OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AvailabilityWindowRow>(window =>
        {
            window.ToTable("availability_windows", table =>
            {
                table.HasCheckConstraint(
                    "ck_availability_windows_day",
                    "day IN ('monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday')");
                table.HasCheckConstraint(
                    "ck_availability_windows_hours", "0 <= start_hour AND start_hour < end_hour AND end_hour <= 24");
            });
            window.HasKey(row => new { row.AccountId, row.Day, row.StartHour }).HasName("pk_availability_windows");
            window.Property(row => row.AccountId).HasColumnName("account_id");
            window.Property(row => row.Day).HasColumnName("day").HasMaxLength(9);
            window.Property(row => row.StartHour).HasColumnName("start_hour");
            window.Property(row => row.EndHour).HasColumnName("end_hour");
            window.HasOne<ProfileRow>().WithMany().HasForeignKey(row => row.AccountId)
                .HasConstraintName("fk_availability_windows_profiles").OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HomeGymRow>(home =>
        {
            home.ToTable("home_gyms", table =>
                table.HasCheckConstraint("ck_home_gyms_position", "position >= 0"));
            home.HasKey(row => new { row.AccountId, row.GymId }).HasName("pk_home_gyms");
            home.Property(row => row.AccountId).HasColumnName("account_id");
            home.Property(row => row.GymId).HasColumnName("gym_id");
            home.Property(row => row.Position).HasColumnName("position");
            home.HasOne<ProfileRow>().WithMany().HasForeignKey(row => row.AccountId)
                .HasConstraintName("fk_home_gyms_profiles").OnDelete(DeleteBehavior.Cascade);
            home.HasOne<GymRow>().WithMany().HasForeignKey(row => row.GymId)
                .HasConstraintName("fk_home_gyms_gyms").OnDelete(DeleteBehavior.Restrict);
            home.HasIndex(row => row.GymId).HasDatabaseName("ix_home_gyms_gym_id");
        });

        modelBuilder.Entity<InviteRow>(invite =>
        {
            invite.ToTable("invites", table =>
            {
                table.HasCheckConstraint("ck_invites_state", "state IN ('pending', 'accepted', 'declined', 'expired')");
                table.HasCheckConstraint(
                    "ck_invites_expiry_cause",
                    "CASE WHEN state = 'expired' THEN expiry_cause IS NOT NULL AND expiry_cause IN ('timeout', 'deletion', "
                    + "'block', 'suspension') ELSE expiry_cause IS NULL END");
                table.HasCheckConstraint(
                    "ck_invites_ended_at", "(ended_at IS NULL) = (state = 'pending')");
                table.HasCheckConstraint("ck_invites_pair", "sender_id <> recipient_id");
            });
            invite.HasKey(row => row.Id).HasName("pk_invites");
            invite.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            invite.Property(row => row.SenderId).HasColumnName("sender_id");
            invite.Property(row => row.RecipientId).HasColumnName("recipient_id");
            invite.Property(row => row.SentAt).HasColumnName("sent_at");
            invite.Property(row => row.State).HasColumnName("state").HasMaxLength(8);
            invite.Property(row => row.ExpiryCause).HasColumnName("expiry_cause").HasMaxLength(16);
            invite.Property(row => row.EndedAt).HasColumnName("ended_at");
            invite.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.SenderId)
                .HasConstraintName("fk_invites_accounts_sender").OnDelete(DeleteBehavior.Cascade);
            invite.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.RecipientId)
                .HasConstraintName("fk_invites_accounts_recipient").OnDelete(DeleteBehavior.Cascade);

            // Leads with sender_id, so it also serves S31's count and the S117 prefilter.
            invite.HasIndex(row => new { row.SenderId, row.RecipientId }).IsUnique().HasDatabaseName(PendingInviteIndex)
                .HasFilter("state = 'pending'");
            invite.HasIndex(row => row.SenderId).HasDatabaseName("ix_invites_sender_id");
            invite.HasIndex(row => row.RecipientId).HasDatabaseName("ix_invites_recipient_id");
        });

        modelBuilder.Entity<ConnectionRow>(connection =>
        {
            connection.ToTable("connections", table =>
            {
                table.HasCheckConstraint("ck_connections_state", "state IN ('active', 'paused', 'ended')");
                table.HasCheckConstraint("ck_connections_pair", "pair_low < pair_high");
            });
            connection.HasKey(row => row.Id).HasName("pk_connections");
            connection.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            connection.Property(row => row.PairLow).HasColumnName("pair_low");
            connection.Property(row => row.PairHigh).HasColumnName("pair_high");
            connection.Property(row => row.State).HasColumnName("state").HasMaxLength(8);
            connection.Property(row => row.CreatedAt).HasColumnName("created_at");
            connection.Property(row => row.InviteId).HasColumnName("invite_id");
            connection.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.PairLow)
                .HasConstraintName("fk_connections_accounts_low").OnDelete(DeleteBehavior.Cascade);
            connection.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.PairHigh)
                .HasConstraintName("fk_connections_accounts_high").OnDelete(DeleteBehavior.Cascade);
            connection.HasOne<InviteRow>().WithOne().HasForeignKey<ConnectionRow>(row => row.InviteId)
                .HasConstraintName("fk_connections_invites").OnDelete(DeleteBehavior.Cascade);
            connection.HasIndex(row => new { row.PairLow, row.PairHigh }).IsUnique().HasDatabaseName(LiveConnectionIndex)
                .HasFilter("state IN ('active', 'paused')");
            // Full indexes for the two account foreign keys: the live-pair index above is partial, so it serves neither.
            connection.HasIndex(row => row.PairLow).HasDatabaseName("ix_connections_pair_low");
            connection.HasIndex(row => row.PairHigh).HasDatabaseName("ix_connections_pair_high");
            connection.HasIndex(row => row.InviteId).IsUnique().HasDatabaseName("ix_connections_invite_id");
        });

        modelBuilder.Entity<SafetyAcknowledgementRow>(acknowledgement =>
        {
            acknowledgement.ToTable("safety_acknowledgements");
            acknowledgement.HasKey(row => row.AccountId).HasName("pk_safety_acknowledgements");
            acknowledgement.Property(row => row.AccountId).HasColumnName("account_id");
            acknowledgement.Property(row => row.AcknowledgedAt).HasColumnName("acknowledged_at");
            acknowledgement.HasOne<AccountRow>().WithOne().HasForeignKey<SafetyAcknowledgementRow>(row => row.AccountId)
                .HasConstraintName("fk_safety_acknowledgements_accounts").OnDelete(DeleteBehavior.Cascade);
        });

        // Neither foreign key cascades: removing an account or a connection never takes the other climber's
        // messages with it (S9, S38); erasure scrubs the account instead.
        modelBuilder.Entity<MessageRow>(message =>
        {
            message.ToTable("messages");
            message.HasKey(row => row.Id).HasName("pk_messages");
            message.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            message.Property(row => row.Position).HasColumnName("position").UseIdentityAlwaysColumn();
            message.Property(row => row.ConnectionId).HasColumnName("connection_id");
            message.Property(row => row.SenderId).HasColumnName("sender_id");
            message.Property(row => row.Text).HasColumnName("text");
            message.Property(row => row.SentAt).HasColumnName("sent_at");
            message.HasOne<ConnectionRow>().WithMany().HasForeignKey(row => row.ConnectionId)
                .HasConstraintName("fk_messages_connections").OnDelete(DeleteBehavior.Restrict);
            message.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.SenderId)
                .HasConstraintName("fk_messages_accounts_sender").OnDelete(DeleteBehavior.Restrict);
            message.HasIndex(row => new { row.ConnectionId, row.Position }).IsUnique().HasDatabaseName("ix_messages_connection_position");
            message.HasIndex(row => row.SenderId).HasDatabaseName("ix_messages_sender_id");
        });

        // S70, in-app. Both accounts cascade: a notification is only the recipient's, about the actor.
        modelBuilder.Entity<NotificationRow>(notification =>
        {
            notification.ToTable("notifications", table => table.HasCheckConstraint("ck_notifications_event", NotificationEventCheck));
            notification.HasKey(row => row.Id).HasName("pk_notifications");
            notification.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            notification.Property(row => row.AccountId).HasColumnName("account_id");
            notification.Property(row => row.Event).HasColumnName("event").HasMaxLength(32);
            notification.Property(row => row.ActorId).HasColumnName("actor_id");
            notification.Property(row => row.SubjectId).HasColumnName("subject_id");
            notification.Property(row => row.CreatedAt).HasColumnName("created_at");
            notification.Property(row => row.SeenAt).HasColumnName("seen_at");
            notification.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.AccountId)
                .HasConstraintName("fk_notifications_accounts").OnDelete(DeleteBehavior.Cascade);
            notification.HasOne<AccountRow>().WithMany().HasForeignKey(row => row.ActorId)
                .HasConstraintName("fk_notifications_accounts_actor").OnDelete(DeleteBehavior.Cascade);

            // Serves the list, newest first, for one account.
            notification.HasIndex(row => new { row.AccountId, row.CreatedAt, row.Id }).HasDatabaseName("ix_notifications_account_created");
            notification.HasIndex(row => row.ActorId).HasDatabaseName("ix_notifications_actor_id");
        });
    }

    private static readonly string NotificationEventCheck =
        $"event IN ('{NotificationNames.InviteReceived}', '{NotificationNames.InviteAccepted}', '{NotificationNames.NewMessage}')";

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
