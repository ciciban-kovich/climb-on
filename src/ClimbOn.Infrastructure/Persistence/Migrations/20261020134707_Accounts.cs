using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClimbOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Accounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    language = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_language", "language IN ('en', 'nb')");
                });

            migrationBuilder.CreateTable(
                name: "legal_documents",
                columns: table => new
                {
                    document = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    text_en = table.Column<string>(type: "text", nullable: false),
                    text_nb = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_documents", x => new { x.document, x.version });
                    table.CheckConstraint("ck_legal_documents_document", "document IN ('terms', 'privacy')");
                    table.CheckConstraint("ck_legal_documents_version", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "legal_acknowledgements",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_acknowledgements", x => new { x.account_id, x.document, x.version });
                    table.ForeignKey(
                        name: "fk_legal_acknowledgements_accounts",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_legal_acknowledgements_legal_documents",
                        columns: x => new { x.document, x.version },
                        principalTable: "legal_documents",
                        principalColumns: new[] { "document", "version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "legal_documents",
                columns: new[] { "document", "version", "published_at", "text_nb", "text_en" },
                values: new object[,]
                {
                    { "privacy", 1, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Personvernerklæring, versjon 1\n\nClimbOn behandler opplysningene du gir for å foreslå klatrepartnere, la deg invitere og chatte med dem, og varsle deg. Behandlingsgrunnlaget er oppfyllelse av avtalen med deg.\n\nVi lagrer e-postadressen din, passord-hash, fødselsdato, språk, profil, tilgjengelighet, invitasjoner, forbindelser og meldingene du skriver. Fødselsdatoen brukes bare til å sjekke at du er minst 18 år; den vises ikke til andre klatrere. E-postadressen din vises aldri til andre klatrere.\n\nOpplysningene lagres i EØS. E-post sendes gjennom en leverandør i EØS.\n\nDu kan laste ned opplysningene dine og slette kontoen din i appen. Etter en sletteforespørsel slettes kontoen etter 30 dager.", "Privacy policy, version 1\n\nClimbOn processes the data you give it to suggest climbing partners, to let you invite and chat with them, and to notify you. The legal basis is the performance of our agreement with you.\n\nWe store your email address, password hash, date of birth, language, profile, availability, invites, connections and the messages you write. Your date of birth is used only to check that you are at least 18; it is not shown to other climbers. Your email address is never shown to other climbers.\n\nData is stored in the EEA. Email is sent through a provider in the EEA.\n\nYou can download your data and delete your account in the app. After a deletion request your account is erased after 30 days." },
                    { "terms", 1, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Brukervilkår, versjon 1\n\nClimbOn hjelper klatrere i Norge med å finne klatrepartnere. Du må være minst 18 år for å registrere deg.\n\nDu er ansvarlig for det du skriver og for hvordan du møter og klatrer med andre. Brattkort som vises i appen er oppgitt av klatreren selv og kontrolleres ikke av ClimbOn. Sjekk selv sikringskompetansen til partneren din, både inne og ute.\n\nIkke trakaser andre, opprett falske profiler eller oppfør deg utrygt. Profiler og meldinger kan rapporteres, og ClimbOn kan advare eller suspendere en konto som bryter disse vilkårene.\n\nDu kan slette kontoen din når som helst i appen.\n\nTjenesten leveres som den er, uten garanti for tilgjengelighet.", "Terms of service, version 1\n\nClimbOn helps climbers in Norway find climbing partners. To register you must be at least 18 years old.\n\nYou are responsible for what you write and for how you meet and climb with others. Belay certifications shown in the app are self-declared and not checked by ClimbOn. Check your partner's belay competence yourself, indoors and outdoors.\n\nDo not harass others, create fake profiles or behave unsafely. Profiles and messages can be reported, and ClimbOn may warn or suspend an account that breaks these terms.\n\nYou can delete your account at any time in the app.\n\nThe service is provided as is, without guarantees of availability." }
                });

            migrationBuilder.CreateIndex(
                name: "ix_legal_acknowledgements_document_version",
                table: "legal_acknowledgements",
                columns: new[] { "document", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "legal_acknowledgements");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "legal_documents");
        }
    }
}
