using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorIdToPoiAndAudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorId",
                table: "Pois",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorId",
                table: "AudioFiles",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "Pois");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "AudioFiles");
        }
    }
}
