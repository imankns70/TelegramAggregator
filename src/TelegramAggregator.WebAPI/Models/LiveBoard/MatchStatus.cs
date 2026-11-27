namespace TelegramAggregator.WebAPI.Models.LiveBoard
{
    public enum MatchStatus
    {
        Unknown,
        Upcoming,
        Live,
        Halftime,
        ExtraTime,
        PenaltyShootout,
        Finished,
        Postponed,
        Cancelled
    }

    internal static class MatchStatusExtensions
    {
        public static bool IsInProgress(this MatchStatus status) =>
            status is MatchStatus.Live or MatchStatus.Halftime or MatchStatus.ExtraTime or MatchStatus.PenaltyShootout;
    }
}
