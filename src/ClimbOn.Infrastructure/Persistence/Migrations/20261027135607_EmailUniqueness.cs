using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClimbOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmailUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows registered before this migration get Account.NormaliseEmail of their stored address.
            // For an all-ASCII address that is lower(email): IDNA leaves an ASCII domain as it is. A
            // non-ASCII domain needs IDNA, which SQL cannot compute, so the migration stops rather than
            // store a wrong form. Two existing accounts with one address stop it too, at the index.
            migrationBuilder.AddColumn<string>(
                name: "email_normalised",
                table: "accounts",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM accounts WHERE octet_length(email) <> char_length(email)) THEN
                        RAISE EXCEPTION 'An account has a non-ASCII email address, which needs Account.NormaliseEmail.';
                    END IF;
                END $$;
                UPDATE accounts SET email_normalised = lower(email);
                """);
            migrationBuilder.AlterColumn<string>(
                name: "email_normalised",
                table: "accounts",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_email_normalised",
                table: "accounts",
                column: "email_normalised",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_email_normalised",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "email_normalised",
                table: "accounts");
        }
    }
}
