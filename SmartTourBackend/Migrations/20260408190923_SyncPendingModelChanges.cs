using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourBackend.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RouteSessions_DeviceId",
                table: "RouteSessions");

            migrationBuilder.DropIndex(
                name: "IX_RouteSessions_EndedAt",
                table: "RouteSessions");

            migrationBuilder.DropIndex(
                name: "IX_RouteSessions_PoiSequence",
                table: "RouteSessions");

            migrationBuilder.DropIndex(
                name: "IX_RouteSessionPois_PoiId",
                table: "RouteSessionPois");

            migrationBuilder.DropIndex(
                name: "IX_RouteSessionPois_TriggerType",
                table: "RouteSessionPois");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "RouteSessions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "completed");

            migrationBuilder.AlterColumn<string>(
                name: "PoiSequence",
                table: "RouteSessions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.AlterColumn<string>(
                name: "DeviceId",
                table: "RouteSessions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "TriggerType",
                table: "RouteSessionPois",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "TtsScript",
                table: "PoiTranslations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "AudioUrl",
                table: "PoiTranslations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Pois",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddForeignKey(
                name: "FK_RouteSessionPois_RouteSessions_RouteSessionId",
                table: "RouteSessionPois",
                column: "RouteSessionId",
                principalTable: "RouteSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RouteSessionPois_RouteSessions_RouteSessionId",
                table: "RouteSessionPois");

            migrationBuilder.DropColumn(
                name: "AudioUrl",
                table: "PoiTranslations");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Pois");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "RouteSessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "completed",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "PoiSequence",
                table: "RouteSessions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "DeviceId",
                table: "RouteSessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TriggerType",
                table: "RouteSessionPois",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TtsScript",
                table: "PoiTranslations",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_RouteSessionPois_PoiId",
                table: "RouteSessionPois",
                column: "PoiId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteSessionPois_TriggerType",
                table: "RouteSessionPois",
                column: "TriggerType");
        }
    }
}
