using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddTourAndTourPoi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TourPois_PoiId",
                table: "TourPois",
                column: "PoiId");

            migrationBuilder.CreateIndex(
                name: "IX_TourPois_TourId",
                table: "TourPois",
                column: "TourId");

            migrationBuilder.CreateIndex(
                name: "IX_AudioFiles_PoiId",
                table: "AudioFiles",
                column: "PoiId");

            migrationBuilder.AddForeignKey(
                name: "FK_AudioFiles_Pois_PoiId",
                table: "AudioFiles",
                column: "PoiId",
                principalTable: "Pois",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TourPois_Pois_PoiId",
                table: "TourPois",
                column: "PoiId",
                principalTable: "Pois",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TourPois_Tours_TourId",
                table: "TourPois",
                column: "TourId",
                principalTable: "Tours",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AudioFiles_Pois_PoiId",
                table: "AudioFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_TourPois_Pois_PoiId",
                table: "TourPois");

            migrationBuilder.DropForeignKey(
                name: "FK_TourPois_Tours_TourId",
                table: "TourPois");

            migrationBuilder.DropIndex(
                name: "IX_TourPois_PoiId",
                table: "TourPois");

            migrationBuilder.DropIndex(
                name: "IX_TourPois_TourId",
                table: "TourPois");

            migrationBuilder.DropIndex(
                name: "IX_AudioFiles_PoiId",
                table: "AudioFiles");
        }
    }
}
