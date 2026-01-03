using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramAggregator.WebAPI.Migrations
{
    /// <inheritdoc />
    public partial class CreateSerilogTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "log_http_client",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message = table.Column<string>(type: "text", nullable: true),
                    level = table.Column<string>(type: "text", nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    exception = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    http_client = table.Column<string>(type: "text", nullable: true),
                    requested_url = table.Column<string>(type: "text", nullable: true),
                    input_data = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_http_client", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "logs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message = table.Column<string>(type: "text", nullable: true),
                    message_template = table.Column<string>(type: "text", nullable: true),
                    level = table.Column<string>(type: "text", nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    exception = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_log_http_client_requested_url",
                schema: "public",
                table: "log_http_client",
                column: "requested_url");

            migrationBuilder.CreateIndex(
                name: "IX_log_http_client_timestamp",
                schema: "public",
                table: "log_http_client",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_logs_level",
                schema: "public",
                table: "logs",
                column: "level");

            migrationBuilder.CreateIndex(
                name: "IX_logs_timestamp",
                schema: "public",
                table: "logs",
                column: "timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            
        }
    }
}
