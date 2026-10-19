using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClimbOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Postcodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "postcodes",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lon = table.Column<double>(type: "double precision", nullable: false),
                    retired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_postcodes", x => x.code);
                    table.CheckConstraint("ck_postcodes_code", "code ~ '^[0-9]{4}$'");
                    table.CheckConstraint("ck_postcodes_lat", "lat BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_postcodes_lon", "lon BETWEEN -180 AND 180");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "postcodes");
        }
    }
}
