namespace TelegramAggregator.WebAPI.Models.LiveBoard;

internal static class LiveBoardUpdates
{
    private static readonly TimeSpan HighlightDuration = TimeSpan.FromMinutes(2);

    public static void UpdateHighlights(MatchDto current, MatchDto? previous, DateTimeOffset now)
    {
        if (previous == null || !current.Status.IsInProgress())
            return;

        var previousKeys = previous.Events.Select(Key).ToHashSet();
        var newEvents = current.Events.Where(e => e.Type != "status" &&
            (previous.HasDetailedEvents || previous.Status == MatchStatus.Upcoming || e.Type is "goal" or "redcard") &&
            !previousKeys.Contains(Key(e))).ToList();

        if (current.Status != previous.Status && previous.Status != MatchStatus.Unknown)
            newEvents.Add(new MatchEvent { Type = "phase" });

        if (previous.Status.IsInProgress() && previous.HomeScore.HasValue && previous.AwayScore.HasValue
            && current.HomeScore.HasValue && current.AwayScore.HasValue
            && (current.HomeScore != previous.HomeScore || current.AwayScore != previous.AwayScore)
            && !newEvents.Any(e => e.Type == "goal"))
            newEvents.Add(new MatchEvent { Type = "score" });

        if (newEvents.Count > 0)
        {
            current.RecentEvents = newEvents.DistinctBy(Key).ToList();
            current.RecentEventsUntil = now + HighlightDuration;
        }
        else if (previous.RecentEventsUntil > now)
        {
            current.RecentEvents = previous.RecentEvents;
            current.RecentEventsUntil = previous.RecentEventsUntil;
        }
    }

    private static (string Type, string Team, string Player, string OtherPlayer, int Minute, string Extra) Key(MatchEvent e) =>
        (e.Type, e.Team, e.Player, e.OtherPlayer, e.Minute, e.ExtraMinute ?? "");
}
