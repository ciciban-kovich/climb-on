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
            // For an all-ASCII address that is lower(email) or nothing: IDNA leaves a valid ASCII domain as
            // it is, and refuses a label over 63 characters or an xn-- label that is not valid punycode.
            // SQL can compute neither IDNA nor punycode validity, so the migration stops rather than store a
            // wrong form at a non-ASCII address, a label over 63 characters or any xn-- label. Two existing
            // accounts with one address stop it too, at the index.
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
                    IF EXISTS (
                        SELECT 1 FROM accounts
                        WHERE octet_length(email) <> char_length(email)
                            OR EXISTS (
                                SELECT 1 FROM regexp_split_to_table(split_part(email, '@', 2), '\.') AS label
                                WHERE char_length(label) > 63 OR lower(label) LIKE 'xn--%')) THEN
                        RAISE EXCEPTION 'An account has an email address whose normal form needs Account.NormaliseEmail.';
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
