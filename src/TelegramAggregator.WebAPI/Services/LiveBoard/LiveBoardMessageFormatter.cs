using System.Globalization;
using System.Text;
using TelegramAggregator.WebAPI.Models.LiveBoard;

namespace TelegramAggregator.WebAPI.Services.LiveBoard;

public interface ILiveBoardMessageFormatter
{
    string Format(List<MatchDto> matches, DateTimeOffset? lastFetch,
        int liveRefreshSeconds = LiveBoardSettings.DefaultLiveRefreshSeconds, DateTimeOffset? boardTime = null,
        string? yesterday = null, bool animateEvents = false, int failedCompetitions = 0);
}

public class LiveBoardMessageFormatter : ILiveBoardMessageFormatter
{
    private const int MessageLimit = 4096;
    private const string RightToLeft = "\u200F";

    public string Format(List<MatchDto> matches, DateTimeOffset? lastFetch,
        int liveRefreshSeconds = LiveBoardSettings.DefaultLiveRefreshSeconds, DateTimeOffset? boardTime = null,
        string? yesterday = null, bool animateEvents = false, int failedCompetitions = 0)
    {
        var heading = new StringBuilder(FormatHeading(boardTime ?? DateTimeOffset.UtcNow, liveRefreshSeconds));
        heading.AppendLine();
        if (lastFetch is { } fetchedAt)
            heading.AppendLine($"🕒 آخرین به‌روزرسانی {FormatClock(fetchedAt)} به وقت تهران");
        if (failedCompetitions > 0)
            heading.AppendLine($"⚠️ به‌روزرسانی {ToPersianNumerals(failedCompetitions.ToString())} رقابت از FotMob ناموفق بود.");

        if (matches.Count > 0 && matches.All(match => match.Status.IsInProgress()))
            heading.AppendLine("\n🔴 <b>بازی‌های زنده</b>");
        else if (matches.Count > 0)
            heading.AppendLine("\n📅 <b>برنامهٔ بازی‌های امروز</b>");
        else
            heading.AppendLine("\nفعلاً بازی‌ای برای امروز در دسترس نیست.");

        var suffix = yesterday == null ? "" : $"\n\n<blockquote expandable>{yesterday}</blockquote>";
        var today = RenderBoard(heading.ToString(), matches, MessageLimit - suffix.Length, animateEvents: animateEvents);
        if (!animateEvents)
            return today + suffix;

        var rich = ToRichParagraphs(today);
        return yesterday == null ? rich : rich +
            $"<details><summary>🗓️ بازی‌های دیروز</summary>{ToRichParagraphs(yesterday[(yesterday.IndexOf('\n') + 1)..])}</details>";
    }

    internal static string FormatYesterday(DateTimeOffset day, List<MatchDto> matches)
    {
        var heading = new StringBuilder($"🗓️ <b>بازی‌های دیروز</b>\n{FormatDate(day)}\n");
        if (matches.Count == 0)
            heading.AppendLine("فعلاً بازی‌ای برای دیروز در دسترس نیست.");

        return RenderBoard(heading.ToString(), matches, 1750, includeArchiveStatistics: true);
    }

    private static string RenderBoard(string heading, List<MatchDto> matches, int messageLimit,
        bool includeArchiveStatistics = false, bool animateEvents = false)
    {
        var sections = matches.OrderBy(match => match.CompetitionDisplayOrder)
            .ThenBy(match => match.CompetitionName, StringComparer.Ordinal)
            .ThenBy(match => match.StartUtc)
            .ThenBy(match => match.SourceMatchId, StringComparer.Ordinal)
            .Select(match => (Icon: CompetitionIcon(match.SourceCompetitionId), Match: match))
            .GroupBy(row => (row.Match.CompetitionName, row.Icon))
            .ToList();

        var full = Render(matches.Count, withEvents: true, withStatistics: true);
        if (full.Length <= messageLimit)
            return full;

        var eventsOnly = Render(matches.Count, withEvents: true);
        if (eventsOnly.Length <= messageLimit)
            return eventsOnly;

        var compact = Render(matches.Count, withEvents: false);
        if (compact.Length <= messageLimit)
            return compact;

        var scoresOnly = Render(matches.Count, withEvents: false, withHighlights: false);
        if (scoresOnly.Length <= messageLimit)
            return scoresOnly;

        var minimum = 0;
        var maximum = matches.Count - 1;
        while (minimum < maximum)
        {
            var middle = minimum + (maximum - minimum + 1) / 2;
            if (Render(middle, withEvents: false, withHighlights: false).Length <= messageLimit)
                minimum = middle;
            else
                maximum = middle - 1;
        }

        return Render(minimum, withEvents: false, withHighlights: false);

        string Render(int limit, bool withEvents, bool withStatistics = false, bool withHighlights = true)
        {
            var text = new StringBuilder(heading);
            var remaining = limit;
            foreach (var section in sections)
            {
                if (remaining == 0)
                    break;

                var name = HtmlEncode(PersianTeamNames.Translate(section.Key.CompetitionName));
                text.AppendLine($"\n{section.Key.Icon} <b>{name}</b>");
                foreach (var row in section.Take(remaining))
                {
                    var matchLine = FormatMatchLine(row.Match);
                    var highlighted = withHighlights && row.Match.RecentEvents.Count > 0;
                    text.AppendLine(highlighted ? $"✨ {matchLine}" : matchLine);
                    if (highlighted)
                    {
                        AppendHighlights(text, row.Match);
                        if (animateEvents && row.Match.Status.IsInProgress())
                        {
                            foreach (var type in row.Match.RecentEvents.Select(e => e.Type).Where(IsAnimatedEvent).Distinct())
                                text.AppendLine($"<video src=\"tg://video?id={type}\"></video>");
                        }
                    }
                    if (withEvents)
                        AppendEvents(text, row.Match);
                    if (withStatistics && (includeArchiveStatistics || row.Match.Status.IsInProgress())
                        && row.Match.Statistics is { } statistics)
                        AppendStatistics(text, statistics);
                    remaining--;
                }
            }

            if (limit < matches.Count)
                text.AppendLine($"\nو {ToPersianNumerals((matches.Count - limit).ToString())} بازی دیگر در این پیام جا نشد.");

            return text.ToString().TrimEnd();
        }
    }

    internal static bool IsAnimatedEvent(string type) => type is "goal" or "redcard";

    private static string ToRichParagraphs(string html) =>
        string.Concat(html.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length != 0)
            .Select(line => line.StartsWith("<video ", StringComparison.Ordinal) ? line : $"<p>{line}</p>"));

    internal static string FormatHeading(DateTimeOffset boardTime, int liveRefreshSeconds)
    {
        var interval = ToPersianNumerals(Math.Max(1, liveRefreshSeconds).ToString());
        return $"⚽ <b>فوتبال امروز</b>\n📅 {FormatDate(boardTime)}\n" +
            $"🔄 هنگام بازی، وضعیت مسابقه‌ها حدود هر {interval} ثانیه بررسی می‌شود.";
    }

    private static string FormatDate(DateTimeOffset time) =>
        ToPersianNumerals(LiveBoardMatchDay.ToTehran(time)
            .ToString("dddd d MMMM yyyy", CultureInfo.GetCultureInfo("fa-IR")));

    private static string FormatMatchLine(MatchDto match)
    {
        var home = TeamName(match.HomeTeam, match.HomeTeamFa, "میزبان");
        var away = TeamName(match.AwayTeam, match.AwayTeamFa, "مهمان");
        var time = FormatTimePart(match);
        if (match.Status is MatchStatus.Upcoming or MatchStatus.Postponed or MatchStatus.Cancelled or MatchStatus.Unknown)
            return $"{RightToLeft}<b>{time}</b> · {home} – {away}";

        var score = FormatScore(match);
        return $"{RightToLeft}<b>{time}</b> · {home} <b>{score}</b> {away}";
    }

    private static void AppendHighlights(StringBuilder text, MatchDto match)
    {
        foreach (var e in match.RecentEvents.OrderBy(e => e.Minute))
        {
            var (icon, description) = e.Type switch
            {
                "goal" => ("⚽", "گل"),
                "redcard" => ("🟥", "اخراج"),
                "yellowcard" => ("🟨", "کارت زرد"),
                "substitution" => ("🔁", "تعویض"),
                "var" => ("📺", "بازبینی ویدئویی"),
                "missedpenalty" => ("❌", "پنالتی از دست رفت"),
                "shootoutgoal" => ("⚽", "پنالتی گل شد"),
                "injury" => ("🩹", "مصدومیت"),
                "score" => ("📣", "نتیجه تغییر کرد"),
                "phase" => ("⏱", match.Status == MatchStatus.Live ? "بازی در جریان است" : FormatTimePart(match)),
                _ => ("📣", "رویداد بازی")
            };
            var team = string.IsNullOrWhiteSpace(e.Team) ? "" : $" {EventTeam(e.Team, match)}";
            var player = string.IsNullOrWhiteSpace(e.Player) ? "" : $" · {HtmlEncode(PersianTeamNames.Translate(e.Player))}";
            if (e.Type == "substitution" && !string.IsNullOrWhiteSpace(e.OtherPlayer))
                player += $" به‌جای {HtmlEncode(PersianTeamNames.Translate(e.OtherPlayer))}";
            var minute = e.Minute > 0 ? $" · {ToPersianNumerals(e.DisplayMinute)}′" : "";
            text.AppendLine($"{RightToLeft}{icon} <b>تازه · {description}{team}</b>{player}{minute}");
        }
    }

    private static void AppendEvents(StringBuilder text, MatchDto match)
    {
        foreach (var group in match.Events.Where(e => e.Type is "goal" or "redcard")
            .GroupBy(e => (e.Type, Team: EventTeam(e.Team, match))))
        {
            var icon = group.Key.Type == "goal" ? "⚽" : "🟥";
            var label = group.Key.Type == "goal" ? "گل" : "اخراج";
            var minutes = string.Join("، ", group.OrderBy(e => e.Minute).Select(e => ToPersianNumerals(e.DisplayMinute)));
            text.AppendLine($"{RightToLeft}{icon} {label} {group.Key.Team} · {minutes}′");
        }
    }

    private static void AppendStatistics(StringBuilder text, MatchStatistics statistics)
    {
        var parts = new List<string>();
        Add("مالکیت توپ", statistics.Possession, "٪");
        Add("شوت", statistics.TotalShots);
        Add("شوت در چارچوب", statistics.ShotsOnTarget);
        Add("کرنر", statistics.Corners);
        if (parts.Count > 0)
            text.AppendLine($"{RightToLeft}📊 {string.Join(" · ", parts)}");

        void Add(string label, MatchStatistic? value, string unit = "")
        {
            if (value is { } stat)
                parts.Add($"{label} {ToPersianNumerals($"{stat.Home}{unit}–{stat.Away}{unit}")}");
        }
    }

    private static string EventTeam(string team, MatchDto match)
    {
        if (string.Equals(team, match.HomeTeam, StringComparison.OrdinalIgnoreCase))
            return TeamName(match.HomeTeam, match.HomeTeamFa, "میزبان");
        if (string.Equals(team, match.AwayTeam, StringComparison.OrdinalIgnoreCase))
            return TeamName(match.AwayTeam, match.AwayTeamFa, "مهمان");
        return TeamName(team, "", "تیم نامشخص");
    }

    private static string TeamName(string original, string persian, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(persian) ? original : persian;
        return HtmlEncode(PersianTeamNames.Translate(string.IsNullOrWhiteSpace(name) ? fallback : name));
    }

    private static string FormatTimePart(MatchDto match) => match.Status switch
    {
        MatchStatus.Live => FormatLiveMinute(match),
        MatchStatus.Halftime => "بین دو نیمه",
        MatchStatus.ExtraTime => "وقت اضافه",
        MatchStatus.PenaltyShootout => "ضربات پنالتی",
        MatchStatus.Finished => "پایان",
        MatchStatus.Postponed => "به تعویق افتاده",
        MatchStatus.Cancelled => "لغو شده",
        MatchStatus.Upcoming => FormatClock(match.StartUtc),
        _ => "وضعیت نامشخص"
    };

    private static string FormatClock(DateTimeOffset utc) =>
        ToPersianNumerals(LiveBoardMatchDay.ToTehran(utc).ToString("HH:mm"));

    private static string FormatLiveMinute(MatchDto match)
    {
        if (match.Minute is not int minute)
            return "در جریان";

        var extra = match.Events.FirstOrDefault(e => e.Type == "status")?.ExtraMinute;
        var displayedMinute = string.IsNullOrWhiteSpace(extra) ? minute.ToString() : $"{minute}+{extra}";
        return $"دقیقهٔ {ToPersianNumerals(displayedMinute)}";
    }

    private static string FormatScore(MatchDto match)
    {
        if (match.HomeScore is not int home || match.AwayScore is not int away)
            return "–";

        var score = $"{home}–{away}";
        if (match.HomeScoreEt is int homeEt && match.AwayScoreEt is int awayEt)
            score += $" ({homeEt}–{awayEt})";
        if (match.HomeScorePen is int homePen && match.AwayScorePen is int awayPen)
            score += $" (پنالتی {homePen}–{awayPen})";
        return ToPersianNumerals(score);
    }

    private static string CompetitionIcon(string id) => id switch
    {
        "523" => "🇮🇷",
        "47" => "🇬🇧",
        "87" => "🇪🇸",
        "55" => "🇮🇹",
        "54" => "🇩🇪",
        "53" => "🇫🇷",
        "77" or "114" => "🌍",
        _ => "🏆"
    };

    private static string ToPersianNumerals(string text) =>
        string.Concat(text.Select(c => c switch
        {
            >= '0' and <= '9' => (char)('۰' + c - '0'),
            >= '٠' and <= '٩' => (char)('۰' + c - '٠'),
            _ => c
        }));

    private static string HtmlEncode(string text) => ToPersianNumerals(text)
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
