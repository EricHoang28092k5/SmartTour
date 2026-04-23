using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartTourBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorPremiumMomoFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPremium",
                table: "Pois",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PremiumActivatedAt",
                table: "Pois",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PremiumExpiresAt",
                table: "Pois",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "vendor_premium_orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PoiId = table.Column<int>(type: "integer", nullable: false),
                    VendorUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MoMoTransId = table.Column<long>(type: "bigint", nullable: true),
                    RawIpnPayload = table.Column<string>(type: "text", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_premium_orders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vendor_premium_orders_OrderId",
                table: "vendor_premium_orders",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vendor_premium_orders_PoiId_Status_CreatedAt",
                table: "vendor_premium_orders",
                columns: new[] { "PoiId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vendor_premium_orders");

            migrationBuilder.DropColumn(
                name: "IsPremium",
                table: "Pois");

            migrationBuilder.DropColumn(
                name: "PremiumActivatedAt",
                table: "Pois");

            migrationBuilder.DropColumn(
                name: "PremiumExpiresAt",
                table: "Pois");
        }
    }
}
