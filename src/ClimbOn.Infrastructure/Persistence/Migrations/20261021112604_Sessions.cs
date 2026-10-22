using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClimbOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Accounts registered before this migration have had no authenticated request yet:
            // their last activity is their registration.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_activity_at",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);
            migrationBuilder.Sql("UPDATE accounts SET last_activity_at = registered_at;");
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "last_activity_at",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.token_hash);
                    table.CheckConstraint("ck_sessions_token_hash", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_sessions_accounts",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_account_id",
                table: "sessions",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropColumn(
                name: "last_activity_at",
                table: "accounts");
        }
    }
}
