using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartTourAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorWalletTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "vendor_premium_orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'vendor_premium_orders' AND column_name = 'orderkind') THEN
        ALTER TABLE vendor_premium_orders ADD COLUMN ""OrderKind"" character varying(32) NOT NULL DEFAULT 'premium';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'vendor_premium_orders' AND column_name = 'poicreationdraftjson') THEN
        ALTER TABLE vendor_premium_orders ADD COLUMN ""PoiCreationDraftJson"" text NULL;
    END IF;
END $$;");

            migrationBuilder.CreateTable(
                name: "vendor_wallet_ledger",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VendorUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeltaVnd = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfterVnd = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_wallet_ledger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_wallets",
                columns: table => new
                {
                    VendorUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BalanceVnd = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_wallets", x => x.VendorUserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vendor_wallet_ledger_VendorUserId_CreatedAt",
                table: "vendor_wallet_ledger",
                columns: new[] { "VendorUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vendor_wallet_ledger");

            migrationBuilder.DropTable(
                name: "vendor_wallets");

            migrationBuilder.DropColumn(
                name: "OrderKind",
                table: "vendor_premium_orders");

            migrationBuilder.DropColumn(
                name: "PoiCreationDraftJson",
                table: "vendor_premium_orders");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "vendor_premium_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);
        }
    }
}
