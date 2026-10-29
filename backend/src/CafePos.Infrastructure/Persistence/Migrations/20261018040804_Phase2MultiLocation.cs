using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CafePos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2MultiLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "LocationId",
                table: "RefreshTokens",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "StripeTerminalLocationId",
                table: "Locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOwner",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Data: existing orders, stock levels and ledger rows already carry a NOT NULL LocationId from Phase 1, so no backfill
            // is needed. Promote the bootstrap admin to Owner only when there is exactly one Admin in the system; with several
            // Admins nobody is promoted automatically (an Owner can then flag users via PUT /users/{id}). The user keeps their
            // StaffAssignment rows, which are simply ignored for an Owner.
            migrationBuilder.Sql(@"
                UPDATE ""AspNetUsers"" SET ""IsOwner"" = TRUE
                WHERE ""Id"" IN (SELECT ""UserId"" FROM ""StaffAssignments"" WHERE ""Role"" = 'Admin')
                  AND (SELECT COUNT(DISTINCT ""UserId"") FROM ""StaffAssignments"" WHERE ""Role"" = 'Admin') = 1;");

            migrationBuilder.CreateTable(
                name: "ItemVariationLocationOverrides",
                columns: table => new
                {
                    ItemVariationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceOverride = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    TrackInventoryOverride = table.Column<bool>(type: "boolean", nullable: true),
                    LowStockThresholdOverride = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemVariationLocationOverrides", x => new { x.ItemVariationId, x.LocationId });
                    table.ForeignKey(
                        name: "FK_ItemVariationLocationOverrides_ItemVariations_ItemVariation~",
                        column: x => x.ItemVariationId,
                        principalTable: "ItemVariations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemVariationLocationOverrides_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemVariationLocationOverrides_LocationId",
                table: "ItemVariationLocationOverrides",
                column: "LocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemVariationLocationOverrides");

            migrationBuilder.DropColumn(
                name: "StripeTerminalLocationId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "IsOwner",
                table: "AspNetUsers");

            migrationBuilder.Sql(@"DELETE FROM ""RefreshTokens"" WHERE ""LocationId"" IS NULL;"); // Owner cross-location sessions

            migrationBuilder.AlterColumn<Guid>(
                name: "LocationId",
                table: "RefreshTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
