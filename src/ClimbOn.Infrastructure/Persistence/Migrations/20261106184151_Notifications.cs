using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClimbOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "actor_id",
                table: "outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "event",
                table: "outbox",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "outbox",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "email");

            // Every row before this migration is an email; the model has no default, so neither does the column.
            migrationBuilder.Sql("ALTER TABLE outbox ALTER COLUMN kind DROP DEFAULT;");

            migrationBuilder.AddColumn<Guid>(
                name: "subject_id",
                table: "outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_event", "event IN ('invite-received', 'invite-accepted', 'new-message')");
                    table.ForeignKey(
                        name: "fk_notifications_accounts",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notifications_accounts_actor",
                        column: x => x.actor_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_actor_id",
                table: "outbox",
                column: "actor_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_event",
                table: "outbox",
                sql: "event IN ('invite-received', 'invite-accepted', 'new-message')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_kind",
                table: "outbox",
                sql: "kind IN ('email', 'notification')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_notification",
                table: "outbox",
                sql: "(kind = 'notification') = (event IS NOT NULL AND actor_id IS NOT NULL AND subject_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_account_created",
                table: "notifications",
                columns: new[] { "account_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_actor_id",
                table: "notifications",
                column: "actor_id");

            migrationBuilder.AddForeignKey(
                name: "fk_outbox_accounts_actor",
                table: "outbox",
                column: "actor_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_outbox_accounts_actor",
                table: "outbox");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_outbox_actor_id",
                table: "outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_event",
                table: "outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_kind",
                table: "outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_notification",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "event",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "subject_id",
                table: "outbox");
        }
    }
}
