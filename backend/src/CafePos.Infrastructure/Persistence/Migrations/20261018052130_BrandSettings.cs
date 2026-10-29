using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CafePos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BrandSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrandSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    BrandName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "BrandSettings",
                columns: new[] { "Id", "BrandName" },
                values: new object[] { 1, "Brew & Bean" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandSettings");
        }
    }
}
