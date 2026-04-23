using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourBackend.Migrations
{
    public partial class AddStreetFoodGapTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS poi_audio_listen_events (
    ""Id"" BIGSERIAL PRIMARY KEY,
    ""PoiId"" integer NOT NULL,
    ""DurationSeconds"" integer NOT NULL,
    ""DeviceId"" character varying(128) NOT NULL DEFAULT '',
    ""CreatedAt"" timestamp with time zone NOT NULL
);");

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS IX_poi_audio_listen_events_dedupe
ON poi_audio_listen_events (""DeviceId"", ""PoiId"", ""DurationSeconds"", ""CreatedAt"");");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS audio_pipeline_jobs (
    ""Id"" BIGSERIAL PRIMARY KEY,
    ""JobType"" character varying(50) NOT NULL,
    ""Status"" character varying(20) NOT NULL,
    ""PoiId"" integer NOT NULL,
    ""TranslationId"" integer NULL,
    ""PayloadJson"" text NOT NULL,
    ""RetryCount"" integer NOT NULL DEFAULT 0,
    ""MaxRetries"" integer NOT NULL DEFAULT 5,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""NextRetryAt"" timestamp with time zone NULL,
    ""ProcessedAt"" timestamp with time zone NULL,
    ""LastError"" text NULL
);");

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS IX_audio_pipeline_jobs_status_retry
ON audio_pipeline_jobs (""Status"", ""NextRetryAt"");");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS script_change_requests (
    ""Id"" BIGSERIAL PRIMARY KEY,
    ""PoiId"" integer NOT NULL,
    ""LanguageCode"" character varying(20) NOT NULL,
    ""NewScript"" text NOT NULL,
    ""Status"" character varying(20) NOT NULL,
    ""CreatedByUserId"" character varying(128) NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""ReviewedByUserId"" character varying(128) NULL,
    ""ReviewedAt"" timestamp with time zone NULL,
    ""RejectReason"" text NULL
);");

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS IX_script_change_requests_lookup
ON script_change_requests (""PoiId"", ""Status"", ""CreatedAt"");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS script_change_requests;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS audio_pipeline_jobs;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS poi_audio_listen_events;");
        }
    }
}
