using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CafePos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BrandColorsAndDesigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Gradient",
                table: "BrandSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "GradientAngle",
                table: "BrandSettings",
                type: "integer",
                nullable: false,
                defaultValue: 135);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryColor",
                table: "BrandSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TertiaryColor",
                table: "BrandSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BrandDesigns",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Document = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandDesigns", x => x.Kind);
                });

            migrationBuilder.UpdateData(
                table: "BrandSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Gradient", "GradientAngle", "SecondaryColor", "TertiaryColor" },
                values: new object[] { false, 135, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandDesigns");

            migrationBuilder.DropColumn(
                name: "Gradient",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "GradientAngle",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "SecondaryColor",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "TertiaryColor",
                table: "BrandSettings");
        }
    }
}
