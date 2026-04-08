using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartTourBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RouteSessionPois",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RouteSessionId = table.Column<int>(type: "integer", nullable: false),
                    PoiId = table.Column<int>(type: "integer", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DwellSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteSessionPois", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RouteSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PoiSequence = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    StopCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "completed")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RouteSessionPois_PoiId",
                table: "RouteSessionPois",
                column: "PoiId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteSessionPois_RouteSessionId",
                table: "RouteSessionPois",
                column: "RouteSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteSessionPois_TriggerType",
                table: "RouteSessionPois",
                column: "TriggerType");

            migrationBuilder.CreateIndex(
                name: "IX_RouteSessions_DeviceId",
                table: "RouteSessions",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteSessions_EndedAt",
                table: "RouteSessions",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RouteSessions_PoiSequence",
                table: "RouteSessions",
                column: "PoiSequence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RouteSessionPois");

            migrationBuilder.DropTable(
                name: "RouteSessions");
        }
    }
}
