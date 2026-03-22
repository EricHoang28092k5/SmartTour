using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorIdToTour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorId",
                table: "Tours",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "Tours");
        }
    }
}
