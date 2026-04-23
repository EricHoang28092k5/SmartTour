using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddPoiApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalNote",
                table: "Pois",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "Pois",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "approved");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Pois",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                table: "Pois",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pois_ApprovalStatus",
                table: "Pois",
                column: "ApprovalStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pois_ApprovalStatus",
                table: "Pois");

            migrationBuilder.DropColumn(
                name: "ApprovalNote",
                table: "Pois");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Pois");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Pois");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "Pois");
        }
    }
}
