using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramAggregator.WebAPI.Migrations
{
    /// <inheritdoc />
    public partial class FixAndCreateLogHttpClientTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PROBLEM: the previous version of this migration called CreateTable("log_http_client")
            // but the first migration already created that table, causing "relation already exists"
            // on every startup and crashing db.Database.Migrate().
            //
            // FIX: use ADD COLUMN IF NOT EXISTS — safe to run even if already applied.

            migrationBuilder.Sql(@"
                ALTER TABLE public.log_http_client
                    ADD COLUMN IF NOT EXISTS message_template text;
            ");

            // Upgrade column types to be more efficient (idempotent via USING cast)
            migrationBuilder.Sql(@"
                ALTER TABLE public.log_http_client
                    ALTER COLUMN level TYPE varchar(32),
                    ALTER COLUMN timestamp TYPE timestamptz USING timestamp AT TIME ZONE 'UTC',
                    ALTER COLUMN properties TYPE jsonb USING properties::jsonb;
            ");

            // Same upgrades for the logs table
            migrationBuilder.Sql(@"
                ALTER TABLE public.logs
                    ALTER COLUMN level TYPE varchar(32),
                    ALTER COLUMN timestamp TYPE timestamptz USING timestamp AT TIME ZONE 'UTC',
                    ALTER COLUMN properties TYPE jsonb USING properties::jsonb;
            ");

            // Indexes — IF NOT EXISTS prevents failure on re-run
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_log_http_client_timestamp""
                    ON public.log_http_client (timestamp DESC);

                CREATE INDEX IF NOT EXISTS ""IX_log_http_client_requested_url""
                    ON public.log_http_client (requested_url);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "log_http_client", schema: "public");

            // revert تغییرات logs
            migrationBuilder.AlterColumn<string>(
                name: "properties",
                schema: "public",
                table: "logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            // ... سایر revertها
        }
    }
}
