using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CafePos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BrandingAssetsAndTimeClock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoContentType",
                table: "BrandSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LogoData",
                table: "BrandSettings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryColor",
                table: "BrandSettings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Tagline",
                table: "BrandSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TarpaulinContentType",
                table: "BrandSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "TarpaulinData",
                table: "BrandSettings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "BrandSettings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "ClockPinFailedCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ClockPinHash",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClockPinLockedUntil",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TimeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClockInAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClockOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeEntries_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "BrandSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "LogoContentType", "LogoData", "PrimaryColor", "Tagline", "TarpaulinContentType", "TarpaulinData", "UpdatedAt" },
                values: new object[] { null, null, "#3b2417", null, null, null, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_LocationId_UserId_ClockInAt",
                table: "TimeEntries",
                columns: new[] { "LocationId", "UserId", "ClockInAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_OneOpenPerUserLocation",
                table: "TimeEntries",
                columns: new[] { "UserId", "LocationId" },
                unique: true,
                filter: "\"ClockOutAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "LogoContentType",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "LogoData",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "PrimaryColor",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "Tagline",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "TarpaulinContentType",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "TarpaulinData",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "ClockPinFailedCount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ClockPinHash",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ClockPinLockedUntil",
                table: "AspNetUsers");
        }
    }
}
