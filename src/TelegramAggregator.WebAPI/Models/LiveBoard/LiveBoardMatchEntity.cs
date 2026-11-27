using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramAggregator.WebAPI.Models.LiveBoard
{
    [Table("LiveBoardMatches")]
    public class LiveBoardMatchEntity
    {
        public long Id { get; set; }
        public string SourceMatchId { get; set; } = "";
        public string SourceCompetitionId { get; set; } = "";
        public string CompetitionName { get; set; } = "";
        public string HomeTeam { get; set; } = "";
        public string AwayTeam { get; set; } = "";
        public string HomeTeamFa { get; set; } = "";
        public string AwayTeamFa { get; set; } = "";
        public DateTimeOffset StartUtc { get; set; }
        public int Status { get; set; }
        public int? Minute { get; set; }
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public int? HomeScoreEt { get; set; }
        public int? AwayScoreEt { get; set; }
        public int? HomeScorePen { get; set; }
        public int? AwayScorePen { get; set; }
        public string EventsJson { get; set; } = "[]";
        public string SourceUrl { get; set; } = "";
        public string DetailUrl { get; set; } = "";
        public DateTimeOffset FetchedAt { get; set; }
    }
}
