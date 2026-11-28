using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using TelegramAggregator.WebAPI.Models.LiveBoard;

namespace TelegramAggregator.WebAPI.Services.LiveBoard;

public class FotMobScraperService(HttpClient http, ILogger<FotMobScraperService> logger) : IFotMobScraperService
{
    private static readonly Uri BaseUrl = new("https://www.fotmob.com");
    private static readonly TimeSpan DetailBudget = TimeSpan.FromSeconds(12);
    private static readonly Regex PageData = new(
        """<script\b[^>]*\bid\s*=\s*["']__NEXT_DATA__["'][^>]*>(?<json>.*?)</script>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public Task<List<MatchDto>> GetMatchesFromCompetitionPageAsync(
        string competitionUrl, string sourceCompetitionId, string competitionName,
        CancellationToken ct = default)
        => ReadCompetitionAsync(competitionUrl, sourceCompetitionId, competitionName, null, ct);

    public Task<List<MatchDto>> GetMatchesForDayAsync(
        LiveBoardCompetition competition, DateTimeOffset day, CancellationToken ct)
        => ReadCompetitionAsync(competition.SourceUrl, competition.SourceCompetitionId, competition.Name, day, ct);

    private async Task<List<MatchDto>> ReadCompetitionAsync(
        string competitionUrl, string sourceCompetitionId, string competitionName,
        DateTimeOffset? archiveDay, CancellationToken ct)
    {
        var day = archiveDay ?? DateTimeOffset.UtcNow;
        var fixtures = await LoadCalendarAsync(competitionUrl, ct);
        var matches = ParseFixtures(fixtures, sourceCompetitionId, competitionName, day);

        using var details = CancellationTokenSource.CreateLinkedTokenSource(ct);
        details.CancelAfter(DetailBudget);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            if (!match.Status.IsInProgress() && !(archiveDay.HasValue && match.Status == MatchStatus.Finished))
                continue;

            try
            {
                var detail = ParseMatchDetail(await LoadPageAsync(match.DetailUrl, details.Token),
                    match.DetailUrl, match.SourceMatchId, sourceCompetitionId);
                if (detail.StartUtc != match.StartUtc)
                    continue;

                detail.CompetitionName = competitionName;
                matches[index] = detail;
            }
            catch (OperationCanceledException) when (details.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Detail deadline reached for competition {CompetitionId}.", sourceCompetitionId);
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not read match details for {MatchId}. Keeping the FotMob fixture.", match.SourceMatchId);
            }
        }

        ct.ThrowIfCancellationRequested();
        return matches;
    }

    public async Task<MatchDto?> GetMatchDetailAsync(
        string detailUrl, string? sourceMatchId, string sourceCompetitionId, CancellationToken ct = default)
    {
        return ParseMatchDetail(await LoadPageAsync(detailUrl, ct), detailUrl, sourceMatchId, sourceCompetitionId);
    }

    private async Task<JsonElement> LoadCalendarAsync(string url, CancellationToken ct)
    {
        try
        {
            return await LoadPageAsync(url, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && IsTransientPageFailure(ex))
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            return await LoadPageAsync(url, ct);
        }
    }

    private static bool IsTransientPageFailure(Exception ex) => ex is OperationCanceledException
        or HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError };

    private async Task<JsonElement> LoadPageAsync(string url, CancellationToken ct)
    {
        var html = await http.GetStringAsync(url, ct);
        var pageData = PageData.Match(html);
        if (!pageData.Success)
            throw new InvalidOperationException($"Match data is missing from {url}.");

        using var document = JsonDocument.Parse(pageData.Groups["json"].Value);
        return document.RootElement.GetProperty("props").GetProperty("pageProps").Clone();
    }

    internal static List<MatchDto> ParseFixtures(JsonElement page, string competitionId, string competitionName,
        DateTimeOffset day)
    {
        EnsureCompetition(page.GetProperty("details").GetProperty("id"), competitionId);
        var (dayStart, dayEnd) = LiveBoardMatchDay.GetUtcBounds(day);
        var matches = new List<MatchDto>();
        foreach (var fixture in page.GetProperty("fixtures").GetProperty("allMatches").EnumerateArray())
        {
            var status = fixture.GetProperty("status");
            var start = ReadStart(status.GetProperty("utcTime"));
            if (start < dayStart || start >= dayEnd)
                continue;

            var matchStatus = ReadStatus(status);
            var score = ReadText(Get(status, "scoreStr")).Split('-', StringSplitOptions.TrimEntries);
            int? homeScore = null;
            int? awayScore = null;
            if (score.Length == 2 && int.TryParse(score[0], out var homeScoreValue) && int.TryParse(score[1], out var awayScoreValue))
            {
                homeScore = homeScoreValue;
                awayScore = awayScoreValue;
            }

            var path = fixture.GetProperty("pageUrl").GetString();
            if (path == null || !path.StartsWith("/matches/", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected FotMob match URL.");

            matches.Add(new MatchDto
            {
                SourceMatchId = ReadText(fixture.GetProperty("id")),
                SourceCompetitionId = competitionId,
                CompetitionName = competitionName,
                HomeTeam = fixture.GetProperty("home").GetProperty("name").GetString()!,
                AwayTeam = fixture.GetProperty("away").GetProperty("name").GetString()!,
                StartUtc = start,
                Status = matchStatus,
                HomeScore = homeScore,
                AwayScore = awayScore,
                DetailUrl = new Uri(BaseUrl, path).AbsoluteUri,
                SourceUrl = new Uri(BaseUrl, path).AbsoluteUri
            });
        }
        return matches.DistinctBy(match => match.SourceMatchId).ToList();
    }

    internal static MatchDto ParseMatchDetail(JsonElement page, string detailUrl, string? sourceMatchId, string competitionId)
    {
        var general = page.GetProperty("general");
        var header = page.GetProperty("header");
        var status = header.GetProperty("status");
        var teams = header.GetProperty("teams");
        var id = ReadText(general.GetProperty("matchId"));
        if (sourceMatchId != null && sourceMatchId != id)
            throw new InvalidOperationException("FotMob returned a different match.");
        EnsureCompetition(general.GetProperty("parentLeagueId"), competitionId);

        var match = new MatchDto
        {
            SourceMatchId = id,
            SourceCompetitionId = competitionId,
            HomeTeam = teams[0].GetProperty("name").GetString()!,
            AwayTeam = teams[1].GetProperty("name").GetString()!,
            HomeScore = ReadNumber(teams[0], "score"),
            AwayScore = ReadNumber(teams[1], "score"),
            StartUtc = ReadStart(status.GetProperty("utcTime")),
            Status = ReadStatus(status),
            Statistics = ReadStatistics(page),
            DetailUrl = detailUrl,
            SourceUrl = detailUrl
        };
        var liveTime = Get(status, "liveTime");
        var displayedMinute = ReadText(Get(liveTime, "short"));
        var minute = displayedMinute.Split(':', '+', '’', '\'');
        if (match.Status.IsInProgress() && int.TryParse(minute[0], out var elapsed))
        {
            match.Minute = elapsed;
            if (displayedMinute.Contains('+') && minute.Length > 1)
                match.Events.Add(new MatchEvent { Type = "status", ExtraMinute = minute[1] });
        }

        var penalties = ReadText(Get(status, "penaltyShootoutScore"));
        var split = penalties.Split('-');
        if (split.Length == 2 && int.TryParse(split[0], out var homePen) && int.TryParse(split[1], out var awayPen))
        {
            match.HomeScorePen = homePen;
            match.AwayScorePen = awayPen;
        }

        var events = Get(header, "events");
        AddEvents("homeTeamGoals", "goal", match.HomeTeam);
        AddEvents("awayTeamGoals", "goal", match.AwayTeam);
        AddEvents("homeTeamRedCards", "redcard", match.HomeTeam);
        AddEvents("awayTeamRedCards", "redcard", match.AwayTeam);
        AddDetailedEvents(match, Get(Get(Get(Get(page, "content"), "matchFacts"), "events"), "events"));
        return match;

        void AddEvents(string bucket, string type, string team)
        {
            var groups = Get(events, bucket);
            if (groups.ValueKind != JsonValueKind.Object)
                return;

            foreach (var item in groups.EnumerateObject().SelectMany(group => group.Value.EnumerateArray()))
            {
                if (Get(item, "isPenaltyShootoutEvent").ValueKind == JsonValueKind.True || ReadNumber(item, "time") is not int time)
                    continue;
                var player = ReadText(Get(Get(item, "player"), "name"));
                var extra = ReadNumber(item, "overloadTime");
                var extraMinute = extra > 0 ? extra.ToString() : null;
                if (match.Events.Any(e => e.Type == type && e.Team == team && e.Player == player && e.Minute == time && e.ExtraMinute == extraMinute))
                    continue;
                match.Events.Add(new MatchEvent { Type = type, Team = team, Player = player, Minute = time, ExtraMinute = extraMinute });
            }
        }
    }

    private static void AddDetailedEvents(MatchDto match, JsonElement events)
    {
        if (events.ValueKind != JsonValueKind.Array)
            return;

        match.HasDetailedEvents = true;
        foreach (var item in events.EnumerateArray())
        {
            var sourceType = ReadText(Get(item, "type")).ToLowerInvariant();
            var type = sourceType switch
            {
                "card" when ReadText(Get(item, "card")) == "Yellow" => "yellowcard",
                "substitution" => "substitution",
                "var" or "videoassistantreferee" => "var",
                "missedpenalty" or "penaltymiss" => "missedpenalty",
                "goal" when Get(item, "isPenaltyShootoutEvent").ValueKind == JsonValueKind.True => "shootoutgoal",
                "injury" => "injury",
                _ => ""
            };
            if (type.Length == 0)
                continue;

            var team = Get(item, "isHome").ValueKind switch
            {
                JsonValueKind.True => match.HomeTeam,
                JsonValueKind.False => match.AwayTeam,
                _ => ""
            };
            var player = ReadText(Get(Get(item, "player"), "name"));
            var otherPlayer = "";
            var swap = Get(item, "swap");
            if (type == "substitution" && swap.ValueKind == JsonValueKind.Array && swap.GetArrayLength() >= 2)
            {
                player = ReadText(Get(swap[0], "name"));
                otherPlayer = ReadText(Get(swap[1], "name"));
            }

            var extra = ReadNumber(item, "overloadTime");
            match.Events.Add(new MatchEvent
            {
                Type = type, Team = team, Player = player, OtherPlayer = otherPlayer,
                Minute = ReadNumber(item, "time") ?? 0, ExtraMinute = extra > 0 ? extra.ToString() : null
            });
        }
    }

    private static MatchStatistics? ReadStatistics(JsonElement page)
    {
        var periods = Get(Get(Get(page, "content"), "stats"), "Periods");
        var groups = Get(Get(periods, "All"), "stats");
        if (groups.ValueKind != JsonValueKind.Array)
            return null;

        MatchStatistic? possession = null, totalShots = null, shotsOnTarget = null, corners = null;
        foreach (var group in groups.EnumerateArray())
        {
            var stats = Get(group, "stats");
            if (stats.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var stat in stats.EnumerateArray())
            {
                var values = Get(stat, "stats");
                if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 2
                    || !int.TryParse(ReadText(values[0]).TrimEnd('%'), NumberStyles.None, CultureInfo.InvariantCulture, out var home)
                    || !int.TryParse(ReadText(values[1]).TrimEnd('%'), NumberStyles.None, CultureInfo.InvariantCulture, out var away))
                    continue;

                switch (ReadText(Get(stat, "key")))
                {
                    case "BallPossesion":
                    case "BallPossession":
                        if (home <= 100 && away <= 100)
                            possession ??= new MatchStatistic(home, away);
                        break;
                    case "total_shots":
                        totalShots ??= new MatchStatistic(home, away);
                        break;
                    case "ShotsOnTarget":
                        shotsOnTarget ??= new MatchStatistic(home, away);
                        break;
                    case "corners":
                        corners ??= new MatchStatistic(home, away);
                        break;
                }
            }
        }

        return possession == null && totalShots == null && shotsOnTarget == null && corners == null
            ? null
            : new MatchStatistics(possession, totalShots, shotsOnTarget, corners);
    }

    private static MatchStatus ReadStatus(JsonElement status)
    {
        var reason = Get(status, "reason");
        var code = (ReadText(Get(reason, "short")) + " " + ReadText(Get(reason, "longKey")) + " "
            + ReadText(Get(Get(status, "liveTime"), "short"))).ToLowerInvariant();
        if (code.Contains("postp")) return MatchStatus.Postponed;
        if (Get(status, "cancelled").ValueKind == JsonValueKind.True) return MatchStatus.Cancelled;
        if (Get(status, "finished").ValueKind == JsonValueKind.True) return MatchStatus.Finished;
        if (Get(status, "started").ValueKind == JsonValueKind.False) return MatchStatus.Upcoming;
        if (code.Contains("pen")) return MatchStatus.PenaltyShootout;
        if (code.Contains("extra") || code.Contains("et ")) return MatchStatus.ExtraTime;
        if (code.Contains("halftime") || code.Trim() == "ht") return MatchStatus.Halftime;
        if (Get(status, "started").ValueKind == JsonValueKind.True) return MatchStatus.Live;
        return MatchStatus.Unknown;
    }

    private static void EnsureCompetition(JsonElement id, string competitionId)
    {
        if (ReadText(id) != competitionId)
            throw new InvalidOperationException($"FotMob returned a different competition than {competitionId}.");
    }

    private static DateTimeOffset ReadStart(JsonElement value) =>
        DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();

    private static JsonElement Get(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) ? value : default;

    private static string ReadText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! :
        value.ValueKind == JsonValueKind.Number ? value.GetRawText() : "";

    private static int? ReadNumber(JsonElement element, string key) =>
        int.TryParse(ReadText(Get(element, key)), out var number) ? number : null;
}
