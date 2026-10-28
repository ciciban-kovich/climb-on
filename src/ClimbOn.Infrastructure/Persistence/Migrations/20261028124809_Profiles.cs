using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClimbOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Profiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gyms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    postcode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gyms", x => x.id);
                    table.CheckConstraint("ck_gyms_postcode", "postcode ~ '^[0-9]{4}$'");
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    sex = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    accepted_sexes = table.Column<string[]>(type: "text[]", nullable: false),
                    rope_scale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    certification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    own_gear = table.Column<bool>(type: "boolean", nullable: false),
                    postcode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    max_travel_km = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profiles", x => x.account_id);
                    table.CheckConstraint("ck_profiles_accepted_sexes", "cardinality(accepted_sexes) >= 1 AND accepted_sexes <@ ARRAY['female', 'male', 'other']::text[]");
                    table.CheckConstraint("ck_profiles_certification", "certification IN ('none', 'brattkort-topptau', 'brattkort-led')");
                    table.CheckConstraint("ck_profiles_max_travel_km", "max_travel_km BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_profiles_postcode", "postcode ~ '^[0-9]{4}$'");
                    table.CheckConstraint("ck_profiles_rope_scale", "rope_scale IN ('norwegian', 'french')");
                    table.CheckConstraint("ck_profiles_sex", "sex IN ('female', 'male', 'other')");
                    table.ForeignKey(
                        name: "fk_profiles_accounts",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "availability_windows",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    start_hour = table.Column<int>(type: "integer", nullable: false),
                    end_hour = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability_windows", x => new { x.account_id, x.day, x.start_hour });
                    table.CheckConstraint("ck_availability_windows_day", "day IN ('monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday')");
                    table.CheckConstraint("ck_availability_windows_hours", "0 <= start_hour AND start_hour < end_hour AND end_hour <= 24");
                    table.ForeignKey(
                        name: "fk_availability_windows_profiles",
                        column: x => x.account_id,
                        principalTable: "profiles",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discipline_variants",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    shared = table.Column<bool>(type: "boolean", nullable: false),
                    scale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    own_lowest = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    own_lowest_position = table.Column<int>(type: "integer", nullable: false),
                    own_highest = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    own_highest_position = table.Column<int>(type: "integer", nullable: false),
                    partner_lowest = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    partner_lowest_position = table.Column<int>(type: "integer", nullable: false),
                    partner_highest = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    partner_highest_position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discipline_variants", x => new { x.account_id, x.variant });
                    table.CheckConstraint("ck_discipline_variants_own", "own_lowest_position <= own_highest_position");
                    table.CheckConstraint("ck_discipline_variants_partner", "partner_lowest_position <= partner_highest_position");
                    table.CheckConstraint("ck_discipline_variants_scale", "scale IN ('norwegian', 'french', 'font', 'water-ice')");
                    table.CheckConstraint("ck_discipline_variants_variant", "variant IN ('bouldering-indoor', 'bouldering-outdoor', 'top-rope-indoor', 'top-rope-outdoor', 'lead-indoor', 'lead-outdoor', 'multipitch-outdoor', 'ice-outdoor')");
                    table.ForeignKey(
                        name: "fk_discipline_variants_profiles",
                        column: x => x.account_id,
                        principalTable: "profiles",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "home_gyms",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_home_gyms", x => new { x.account_id, x.gym_id });
                    table.CheckConstraint("ck_home_gyms_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_home_gyms_gyms",
                        column: x => x.gym_id,
                        principalTable: "gyms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_home_gyms_profiles",
                        column: x => x.account_id,
                        principalTable: "profiles",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_home_gyms_gym_id",
                table: "home_gyms",
                column: "gym_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availability_windows");

            migrationBuilder.DropTable(
                name: "discipline_variants");

            migrationBuilder.DropTable(
                name: "home_gyms");

            migrationBuilder.DropTable(
                name: "gyms");

            migrationBuilder.DropTable(
                name: "profiles");
        }
    }
}
