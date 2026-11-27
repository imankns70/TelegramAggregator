using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramAggregator.WebAPI.Migrations
{
    /// <inheritdoc />
    public partial class SeedLiveBoardCompetitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiveBoardCompetitions",
                columns: table => new
                {
                    SourceCompetitionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveBoardCompetitions", x => x.SourceCompetitionId);
                });

            migrationBuilder.InsertData(
                table: "LiveBoardCompetitions",
                columns: new[] { "SourceCompetitionId", "Name", "SourceUrl", "DisplayOrder" },
                values: new object[,]
                {
                    { "523", "لیگ برتر ایران", "https://www.fotmob.com/en-GB/leagues/523/fixtures/persian-gulf", 1 },
                    { "47", "لیگ برتر انگلیس", "https://www.fotmob.com/en-GB/leagues/47/fixtures/premier-league", 2 },
                    { "87", "لالیگا", "https://www.fotmob.com/en-GB/leagues/87/fixtures/laliga", 3 },
                    { "55", "سری آ", "https://www.fotmob.com/en-GB/leagues/55/fixtures/serie", 4 },
                    { "54", "بوندس‌لیگا", "https://www.fotmob.com/en-GB/leagues/54/fixtures/bundesliga", 5 },
                    { "53", "لیگ ۱ فرانسه", "https://www.fotmob.com/en-GB/leagues/53/fixtures/ligue-1", 6 },
                    { "42", "لیگ قهرمانان اروپا", "https://www.fotmob.com/en-GB/leagues/42/fixtures/champions-league", 7 },
                    { "525", "لیگ نخبگان آسیا", "https://www.fotmob.com/en-GB/leagues/525/fixtures/afc-champions-league-elite", 8 },
                    { "9469", "لیگ قهرمانان آسیا ۲", "https://www.fotmob.com/en-GB/leagues/9469/fixtures/afc-champions-league-two", 9 },
                    { "77", "جام جهانی", "https://www.fotmob.com/en-GB/leagues/77/fixtures/world-cup", 10 },
                    { "290", "جام ملت‌های آسیا", "https://www.fotmob.com/leagues/290/fixtures/asian-cup", 11 },
                    { "10197", "مقدماتی جام جهانی آسیا", "https://www.fotmob.com/leagues/10197/fixtures/world-cup-qualification-afc", 12 },
                    { "114", "بازی‌های دوستانهٔ ملی", "https://www.fotmob.com/en-GB/leagues/114/fixtures/friendlies", 13 },
                    { "50", "جام ملت‌های اروپا", "https://www.fotmob.com/en-GB/leagues/50/fixtures/euro", 14 },
                    { "10195", "مقدماتی جام جهانی اروپا", "https://www.fotmob.com/leagues/10195/fixtures/world-cup-qualification-uefa", 15 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveBoardCompetitions");
        }
    }
}
