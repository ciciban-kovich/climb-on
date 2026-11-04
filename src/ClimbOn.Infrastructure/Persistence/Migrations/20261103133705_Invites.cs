using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClimbOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Invites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    expiry_cause = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invites", x => x.id);
                    table.CheckConstraint("ck_invites_ended_at", "(ended_at IS NULL) = (state = 'pending')");
                    table.CheckConstraint("ck_invites_expiry_cause", "CASE WHEN state = 'expired' THEN expiry_cause IS NOT NULL AND expiry_cause IN ('timeout', 'deletion', 'block', 'suspension') ELSE expiry_cause IS NULL END");
                    table.CheckConstraint("ck_invites_pair", "sender_id <> recipient_id");
                    table.CheckConstraint("ck_invites_state", "state IN ('pending', 'accepted', 'declined', 'expired')");
                    table.ForeignKey(
                        name: "fk_invites_accounts_recipient",
                        column: x => x.recipient_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invites_accounts_sender",
                        column: x => x.sender_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "safety_acknowledgements",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_safety_acknowledgements", x => x.account_id);
                    table.ForeignKey(
                        name: "fk_safety_acknowledgements_accounts",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pair_low = table.Column<Guid>(type: "uuid", nullable: false),
                    pair_high = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invite_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connections", x => x.id);
                    table.CheckConstraint("ck_connections_pair", "pair_low < pair_high");
                    table.CheckConstraint("ck_connections_state", "state IN ('active', 'paused', 'ended')");
                    table.ForeignKey(
                        name: "fk_connections_accounts_high",
                        column: x => x.pair_high,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_connections_accounts_low",
                        column: x => x.pair_low,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_connections_invites",
                        column: x => x.invite_id,
                        principalTable: "invites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_connections_invite_id",
                table: "connections",
                column: "invite_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connections_live_pair",
                table: "connections",
                columns: new[] { "pair_low", "pair_high" },
                unique: true,
                filter: "state IN ('active', 'paused')");

            migrationBuilder.CreateIndex(
                name: "ix_connections_pair_high",
                table: "connections",
                column: "pair_high");

            migrationBuilder.CreateIndex(
                name: "ix_connections_pair_low",
                table: "connections",
                column: "pair_low");

            migrationBuilder.CreateIndex(
                name: "ix_invites_pending_pair",
                table: "invites",
                columns: new[] { "sender_id", "recipient_id" },
                unique: true,
                filter: "state = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_invites_recipient_id",
                table: "invites",
                column: "recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_invites_sender_id",
                table: "invites",
                column: "sender_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connections");

            migrationBuilder.DropTable(
                name: "safety_acknowledgements");

            migrationBuilder.DropTable(
                name: "invites");
        }
    }
}
