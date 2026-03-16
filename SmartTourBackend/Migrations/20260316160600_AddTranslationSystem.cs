using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PoiTranslations_LanguageId",
                table: "PoiTranslations",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_PoiTranslations_PoiId",
                table: "PoiTranslations",
                column: "PoiId");

            migrationBuilder.AddForeignKey(
                name: "FK_PoiTranslations_Languages_LanguageId",
                table: "PoiTranslations",
                column: "LanguageId",
                principalTable: "Languages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PoiTranslations_Pois_PoiId",
                table: "PoiTranslations",
                column: "PoiId",
                principalTable: "Pois",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PoiTranslations_Languages_LanguageId",
                table: "PoiTranslations");

            migrationBuilder.DropForeignKey(
                name: "FK_PoiTranslations_Pois_PoiId",
                table: "PoiTranslations");

            migrationBuilder.DropIndex(
                name: "IX_PoiTranslations_LanguageId",
                table: "PoiTranslations");

            migrationBuilder.DropIndex(
                name: "IX_PoiTranslations_PoiId",
                table: "PoiTranslations");
        }
    }
}
