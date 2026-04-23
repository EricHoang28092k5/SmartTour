using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourBackend.Migrations
{
    /// <inheritdoc />
    public partial class HardenAudioListenAntiSpam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_poi_audio_listen_events_DeviceId_PoiId_CreatedAt",
                table: "poi_audio_listen_events",
                columns: new[] { "DeviceId", "PoiId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_poi_audio_listen_events_DeviceId_PoiId_CreatedAt",
                table: "poi_audio_listen_events");
        }
    }
}
