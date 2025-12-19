using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramAggregator.WebAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOffset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_News_GlobalKey",
                table: "News");

            migrationBuilder.DropColumn(
                name: "GlobalKey",
                table: "News");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GlobalKey",
                table: "News",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_News_GlobalKey",
                table: "News",
                column: "GlobalKey",
                unique: true);
        }
    }
}
