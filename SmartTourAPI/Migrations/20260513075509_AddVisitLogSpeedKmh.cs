using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitLogSpeedKmh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SpeedKmh",
                table: "visit_logs",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpeedKmh",
                table: "visit_logs");
        }
    }
}
