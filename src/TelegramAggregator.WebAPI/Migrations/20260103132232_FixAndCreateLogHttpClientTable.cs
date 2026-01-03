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
            // اگر جدول log_http_client وجود نداشته باشه، بساز (اگر وجود داشته باشه، ignore می‌شه)
            migrationBuilder.CreateTable(
                name: "log_http_client",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message = table.Column<string>(type: "text", nullable: true),
                    message_template = table.Column<string>(type: "text", nullable: true),  // اضافه شده برای سازگاری
                    level = table.Column<string>(type: "varchar(32)", nullable: true),     // بهتر از text
                    timestamp = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),  // بهتر برای offset
                    exception = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "jsonb", nullable: true),       // jsonb برای query بهتر
                    http_client = table.Column<string>(type: "text", nullable: true),
                    requested_url = table.Column<string>(type: "text", nullable: true),
                    input_data = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_http_client", x => x.id);
                });

            // اصلاح جدول logs (که وجود داره)
            migrationBuilder.AlterColumn<string>(
                name: "properties",
                schema: "public",
                table: "logs",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "level",
                schema: "public",
                table: "logs",
                type: "varchar(32)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "timestamp",
                schema: "public",
                table: "logs",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            // ایندکس‌ها (اگر قبلاً نبودن)
            migrationBuilder.CreateIndex(
                name: "IX_log_http_client_timestamp",
                schema: "public",
                table: "log_http_client",
                column: "timestamp",
                descending: new[] { true });

            migrationBuilder.CreateIndex(
                name: "IX_log_http_client_requested_url",
                schema: "public",
                table: "log_http_client",
                column: "requested_url");
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
