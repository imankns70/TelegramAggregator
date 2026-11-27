using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramAggregator.WebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveBoardSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiveBoardMatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceMatchId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceCompetitionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompetitionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HomeTeam = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AwayTeam = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HomeTeamFa = table.Column<string>(type: "text", nullable: true),
                    AwayTeamFa = table.Column<string>(type: "text", nullable: true),
                    StartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Minute = table.Column<int>(type: "integer", nullable: true),
                    HomeScore = table.Column<int>(type: "integer", nullable: true),
                    AwayScore = table.Column<int>(type: "integer", nullable: true),
                    HomeScoreEt = table.Column<int>(type: "integer", nullable: true),
                    AwayScoreEt = table.Column<int>(type: "integer", nullable: true),
                    HomeScorePen = table.Column<int>(type: "integer", nullable: true),
                    AwayScorePen = table.Column<int>(type: "integer", nullable: true),
                    EventsJson = table.Column<string>(type: "text", nullable: true),
                    SourceUrl = table.Column<string>(type: "text", nullable: true),
                    DetailUrl = table.Column<string>(type: "text", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveBoardMatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LiveBoardState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveBoardState", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiveBoardMatches_SourceCompetitionId",
                table: "LiveBoardMatches",
                column: "SourceCompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveBoardMatches_SourceMatchId",
                table: "LiveBoardMatches",
                column: "SourceMatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveBoardState_Key",
                table: "LiveBoardState",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveBoardMatches");

            migrationBuilder.DropTable(
                name: "LiveBoardState");
        }
    }
}
