using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartTourAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "visit_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PoiId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lng = table.Column<double>(type: "double precision", nullable: false),
                    VisitTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VisitType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visit_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_visit_logs_PoiId_VisitTime",
                table: "visit_logs",
                columns: new[] { "PoiId", "VisitTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visit_logs");
        }
    }
}
