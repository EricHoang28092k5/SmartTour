using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddPoiToPlayLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "PlayLog",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PlayLog_PoiId",
                table: "PlayLog",
                column: "PoiId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayLog_Pois_PoiId",
                table: "PlayLog",
                column: "PoiId",
                principalTable: "Pois",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayLog_Pois_PoiId",
                table: "PlayLog");

            migrationBuilder.DropIndex(
                name: "IX_PlayLog_PoiId",
                table: "PlayLog");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "PlayLog");
        }
    }
}
