namespace TelegramAggregator.WebAPI.Models.LiveBoard
{
    public class MatchDto
    {
        public string SourceMatchId { get; set; } = "";
        public string SourceCompetitionId { get; set; } = "";
        public string CompetitionName { get; set; } = "";
        public int CompetitionDisplayOrder { get; set; }
        public string HomeTeam { get; set; } = "";
        public string AwayTeam { get; set; } = "";
        public string HomeTeamFa { get; set; } = "";
        public string AwayTeamFa { get; set; } = "";
        public DateTimeOffset StartUtc { get; set; }
        public MatchStatus Status { get; set; } = MatchStatus.Unknown;
        public int? Minute { get; set; }
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public int? HomeScoreEt { get; set; } // extra time score
        public int? AwayScoreEt { get; set; }
        public int? HomeScorePen { get; set; } // penalty shootout score
        public int? AwayScorePen { get; set; }
        public List<MatchEvent> Events { get; set; } = new();
        public bool HasDetailedEvents { get; set; }
        public List<MatchEvent> RecentEvents { get; set; } = new();
        public DateTimeOffset? RecentEventsUntil { get; set; }
        public MatchStatistics? Statistics { get; set; }
        public string SourceUrl { get; set; } = "";
        public string DetailUrl { get; set; } = "";
    }

    public sealed record MatchStatistic(int Home, int Away);

    public sealed record MatchStatistics(
        MatchStatistic? Possession,
        MatchStatistic? TotalShots,
        MatchStatistic? ShotsOnTarget,
        MatchStatistic? Corners);
}
