using System.Net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models.Entities;
using TelegramAggregator.WebAPI.Models.LiveBoard;
using TelegramAggregator.WebAPI.Services;
using TelegramAggregator.WebAPI.Services.LiveBoard;

public class Program
{
    public static async Task Main(string[] args)
    {
        Check(NewsUrl.GetSourceId("https://www.tarafdari.com/node/123") == 2, "Source identity uses article host");
        Check(NewsUrl.GetSourceId("https://football360.ir/post/123") == 4, "Football360 identity");
        Check(NewsUrl.GetSourceId("https://www.varzesh3.ir/news/123") == 1, "New Varzesh3 domain uses the existing source ID");
        Check(NewsUrl.GetSourceId("https://www.varzesh3.com/news/123") == 1, "Legacy Varzesh3 links still use the same ID");

        const string feedUrl = "https://www.varzesh3.ir/rss/all";
        var publishedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds());
        var rss = $"""
            <rss version="2.0"><channel><title>Varzesh3</title><item>
            <title>Match result</title><link>https://www.varzesh3.ir/news/123</link>
            <pubDate>{publishedAt:yyyy-MM-dd'T'HH:mm:ss}</pubDate>
            </item></channel></rss>
            """;
        var parser = new RssParserService(NullLogger<RssParserService>.Instance);
        Check(parser.Parse(rss, feedUrl).Single().PubDate == publishedAt, "Varzesh3 ISO dates without an offset are UTC");

        if (args.Contains("--live-varzesh3", StringComparer.Ordinal))
        {
            using var liveClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var liveHttp = new HttpHelperService(liveClient, NullLogger<HttpHelperService>.Instance);
            var liveFeed = await liveHttp.GetStringAsync(feedUrl);
            var liveItems = parser.Parse(liveFeed, feedUrl).ToList();
            Check(liveItems.Count > 0, "The live Varzesh3 feed returned recent news");
            Check(liveItems.All(item => NewsUrl.GetSourceId(item.Link) == 1), $"Mapped all {liveItems.Count} live items to Varzesh3");
        }

        var url = NewsUrl.Normalize("https://WWW.TARAFDARI.COM/News/ABC?id=1&utm_source=test#section");
        Check(url == "https://www.tarafdari.com/News/ABC?id=1", "Preserve case and identity query, strip tracking");
        Check(NewsUrl.Normalize("https://tarafdari.com/news?id=1")
            != NewsUrl.Normalize("https://tarafdari.com/news?id=2"), "Distinct query-based articles stay distinct");
        try
        {
            NewsUrl.GetSourceId("https://tarafdari.com.example.org/post/1");
            throw new Exception("Unrecognized host was accepted.");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("PASS: Reject unrelated source host");
        }

        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var helper = new HttpHelperService(client, NullLogger<HttpHelperService>.Instance);
        var content = await helper.GetStringAsync("https://tarafdari.com/rss?category=football");
        Check(content == "<rss />", "HTTP response decoding");
        Check(handler.RequestedUri.Query == "?category=football", "Feed query stays intact");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await helper.GetStringAsync("https://tarafdari.com/rss", cancellation.Token);
            throw new Exception("Cancellation was swallowed.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("PASS: HTTP cancellation propagates");
        }

        var article = new News
        {
            Title = "قرعه‌کشی لیگ قهرمانان",
            ShortDescription = "به گزارش \"خبر فوری\"، به گزارش \"ورزش سه\"، به گزارش «طرفداری»، هفته پنجم آغاز شد.",
            FullDescription = "<p>به گزارش «ورزش سه»، به گزارش «خبر فوری»، مسابقات ادامه دارد. "
                + new string('م', 240) + "</p><p>خبر فوری، خبر فوری؛ نتایج اعلام شد.</p>"
                + "<p>خبر فوری درباره مسابقه نوشت و سپس خبر فوری نتیجه را منتشر کرد.</p>",
            Link = "https://www.varzesh3.ir/news/123?first=1&second=2"
        };
        var caption = CaptionMakerExtension.GetRichHtml(article);
        var decodedCaption = WebUtility.HtmlDecode(caption);
        Check(decodedCaption.Contains("به گزارش \"خبر فوری\"، هفته پنجم آغاز شد.")
            && decodedCaption.Contains("به گزارش «خبر فوری»، مسابقات ادامه دارد.")
            && decodedCaption.Contains("خبر فوری؛ نتایج اعلام شد.")
            && decodedCaption.Contains("خبر فوری درباره مسابقه نوشت و سپس خبر فوری نتیجه را منتشر کرد."),
            "Adjacent duplicate source mentions collapse while separate mentions remain");
        Check(decodedCaption.StartsWith("<p><b>⚽ قرعه‌کشی لیگ قهرمانان</b></p>"), "Headline uses bold body-size text");
        foreach (var (score, emoji) in new[] { (99, "⚽"), (100, "⭐"), (149, "⭐"), (150, "⚡"), (199, "⚡"), (200, "🔥"), (201, "🔥") })
        {
            article.Score = score;
            var scoredCaption = WebUtility.HtmlDecode(CaptionMakerExtension.GetRichHtml(article));
            Check(scoredCaption.StartsWith($"<p><b>{emoji} {article.Title}</b></p>", StringComparison.Ordinal),
                $"Headline uses {emoji} for score {score}");
        }

        Check(decodedCaption.Contains("<details><summary>🔹 متن کامل خبر</summary><p>")
            && !caption.Contains("href=", StringComparison.OrdinalIgnoreCase),
            "Details use a plain label and blue icon in the expandable block");

        var root = Path.GetFullPath(GetRepositoryRoot());
        var files = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToList();
        foreach (var file in files)
        {
            var tree = CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(file));
            var errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
            Check(errors.Count == 0, "C# syntax: " + Path.GetRelativePath(root, file));
        }

        Console.WriteLine($"Passed URL, HTTP, and caption regressions and syntax checks for {files.Count} C# files.");

        // Live board tests
        var formatter = new LiveBoardMessageFormatter();
        Check(formatter.Format(new List<MatchDto>(), null).Contains("هر ۱۰ ثانیه")
            && formatter.Format(new List<MatchDto>(), null, liveRefreshSeconds: 45).Contains("هر ۴۵ ثانیه"),
            "Board header shows the configured live refresh interval in Persian");

        var liveMatch = new MatchDto
        {
            CompetitionName = "لیگ برتر ایران",
            HomeTeam = "پرسپولیس",
            AwayTeam = "سپاهان",
            HomeTeamFa = "پرسپولیس",
            AwayTeamFa = "سپاهان",
            StartUtc = DateTimeOffset.UtcNow.AddHours(-3),
            Status = MatchStatus.Live,
            Minute = 72,
            HomeScore = 2,
            AwayScore = 1,
            Statistics = new MatchStatistics(new(58, 42), new(9, 5), new(4, 2), new(3, 0)),
            Events = new List<MatchEvent>
            {
                new() { Type = "goal", Team = "پرسپولیس", Player = "آلان", Minute = 18 },
                new() { Type = "goal", Team = "پرسپولیس", Player = "رضا", Minute = 64 },
                new() { Type = "goal", Team = "سپاهان", Player = "علی", Minute = 43 },
                new() { Type = "redcard", Team = "سپاهان", Player = "سعيد", Minute = 68 }
            }
        };
        var liveText = formatter.Format(new List<MatchDto> { liveMatch }, DateTimeOffset.Parse("2026-11-26T18:42:00Z").ToOffset(TimeSpan.FromHours(3)));
        Check(liveText.Contains("دقیقهٔ ۷۲"), "Live match shows minute");
        Check(liveText.Contains("پرسپولیس <b>۲–۱</b> سپاهان"), "Live match shows bold score");
        Check(liveText.Contains("⚽ گل پرسپولیس"), "Goal event uses a Persian label and ball emoji");
        Check(liveText.Contains("🟥 اخراج سپاهان"), "Red card event uses a Persian label and red square emoji");
        Check(liveText.Contains("۱۸"), "Goal minute displayed");
        Check(liveText.Contains("۶۸"), "Red card minute displayed");
        var liveBeforeEvent = new MatchDto
        {
            Status = MatchStatus.Live, HomeScore = 1, AwayScore = 1, HasDetailedEvents = true,
            Events = liveMatch.Events.Where(e => e.Minute != 64).ToList()
        };
        var changedMatch = new MatchDto
        {
            CompetitionName = liveMatch.CompetitionName, HomeTeam = liveMatch.HomeTeam, AwayTeam = liveMatch.AwayTeam,
            Status = MatchStatus.Live, HomeScore = 2, AwayScore = 1, Minute = 72, HasDetailedEvents = true,
            Events = new List<MatchEvent>(liveMatch.Events)
            {
                new() { Type = "substitution", Team = "سپاهان", Player = "رضا", OtherPlayer = "علی", Minute = 71 }
            }
        };
        var checkedAt = DateTimeOffset.Parse("2026-11-27T12:30:00Z");
        LiveBoardUpdates.UpdateHighlights(changedMatch, liveBeforeEvent, checkedAt);
        var changeText = formatter.Format(new List<MatchDto> { changedMatch }, null);
        Check(changedMatch.RecentEvents.Count == 2 && changeText.Contains("تازه · گل پرسپولیس")
            && changeText.Contains("تازه · تعویض سپاهان") && changeText.Contains("رضا به‌جای علی")
            && !changeText.Contains("تازه · اخراج"), "Only newly observed events are highlighted under the match");
        var unchangedMatch = new MatchDto
        {
            Status = MatchStatus.Live, HomeScore = 2, AwayScore = 1, HasDetailedEvents = true,
            Events = changedMatch.Events
        };
        LiveBoardUpdates.UpdateHighlights(unchangedMatch, changedMatch, checkedAt.AddMinutes(1));
        Check(unchangedMatch.RecentEventsUntil == changedMatch.RecentEventsUntil,
            "Repeated snapshots do not restart the event highlight window");
        var laterMatch = new MatchDto
        {
            Status = MatchStatus.Live, HomeScore = 2, AwayScore = 1, HasDetailedEvents = true,
            Events = changedMatch.Events
        };
        LiveBoardUpdates.UpdateHighlights(laterMatch, changedMatch, checkedAt.AddMinutes(2));
        Check(laterMatch.RecentEvents.Count == 0, "Fresh marker expires after two minutes");
        Check(liveText.Contains("📊 مالکیت توپ ۵۸٪–۴۲٪ · شوت ۹–۵ · شوت در چارچوب ۴–۲ · کرنر ۳–۰"),
            "Live statistics use Persian labels and preserve real zero values");
        var denseBoard = Enumerable.Range(1, 40).Select(count =>
            formatter.Format(Enumerable.Repeat(liveMatch, count).ToList(), null))
            .First(text => !text.Contains("📊") && !text.Contains("بازی دیگر در این پیام جا نشد"));
        Check(denseBoard.Contains("⚽ گل پرسپولیس"), "Long boards drop statistics before match events");

        var halftimeMatch = new MatchDto
        {
            CompetitionName = "لیگ نخبگان آسیا",
            HomeTeam = "استقلال",
            AwayTeam = "العین",
            HomeTeamFa = "استقلال",
            AwayTeamFa = "العین",
            StartUtc = DateTimeOffset.UtcNow.AddHours(-2),
            Status = MatchStatus.Halftime,
            HomeScore = 0,
            AwayScore = 0
        };
        var htText = formatter.Format(new List<MatchDto> { halftimeMatch }, DateTimeOffset.UtcNow);
        Check(htText.Contains("بین دو نیمه"), "Halftime status shown");
        Check(!htText.Contains("📊"), "Unavailable statistics are omitted");

        var extraTimeMatch = new MatchDto
        {
            CompetitionName = "لیگ قهرمانان اروپا",
            HomeTeam = "آاِک آتن",
            AwayTeam = "رئال مادرید",
            HomeTeamFa = "آأيك أثينا",
            AwayTeamFa = "رئال مدريد",
            StartUtc = DateTimeOffset.UtcNow.AddHours(-1),
            Status = MatchStatus.ExtraTime,
            HomeScore = 1,
            AwayScore = 1,
            HomeScoreEt = 2,
            AwayScoreEt = 1
        };
        var etText = formatter.Format(new List<MatchDto> { extraTimeMatch }, DateTimeOffset.UtcNow);
        Check(etText.Contains("وقت اضافه"), "Extra time status shown");
        Check(etText.Contains("۱–۱ (۲–۱)"), "Extra time score shown");

        var penaltyMatch = new MatchDto
        {
            CompetitionName = "لیگ قهرمانان آسیا ۲",
            HomeTeam = "الاهلی",
            AwayTeam = "سپاهان",
            HomeTeamFa = "الأهلي",
            AwayTeamFa = "سپاهان",
            StartUtc = DateTimeOffset.UtcNow,
            Status = MatchStatus.PenaltyShootout,
            HomeScore = 1,
            AwayScore = 1,
            HomeScorePen = 3,
            AwayScorePen = 4
        };
        var penText = formatter.Format(new List<MatchDto> { penaltyMatch }, DateTimeOffset.UtcNow);
        Check(penText.Contains("ضربات پنالتی"), "Penalty status shown");
        Check(penText.Contains("پنالتی ۳–۴"), "Penalty score shown");

        var upcomingMatch = new MatchDto
        {
            CompetitionName = "لیگ برتر انگلیس",
            HomeTeam = "Nottingham Forest",
            AwayTeam = "Chelsea",
            HomeTeamFa = "",
            AwayTeamFa = "",
            SourceCompetitionId = "47",
            StartUtc = DateTimeOffset.Parse("2026-11-27T12:45:00Z"),
            Status = MatchStatus.Upcoming
        };
        var upText = formatter.Format(new List<MatchDto> { upcomingMatch }, DateTimeOffset.UtcNow);
        Check(upText.Contains("ناتینگهام فارست – چلسی"), "Upcoming match translates FotMob team names");
        Check(upText.Contains("۱۶:۱۵") && upText.Contains("📅 <b>برنامهٔ بازی‌های امروز</b>")
            && upText.Contains("🇬🇧 <b>لیگ برتر انگلیس</b>"), "Upcoming view uses Persian time and styled sections");
        Check(PersianTeamNames.Translate("Borussia Mönchengladbach") == "بوروسیا مونشن‌گلادباخ"
            && PersianTeamNames.Translate("Aluminium Arak") == "آلومینیوم اراک"
            && !System.Text.RegularExpressions.Regex.IsMatch(PersianTeamNames.Translate("Futbol Žilina"), "[A-Za-z]"),
            "Recognized names are localized and new Latin names are transliterated");
        var midnightUtc = DateTimeOffset.Parse("2026-11-27T20:30:00Z");
        var beforeMidnight = LiveBoardMatchDay.GetUtcBounds(midnightUtc.AddSeconds(-1));
        var afterMidnight = LiveBoardMatchDay.GetUtcBounds(midnightUtc);
        Check(beforeMidnight.End == midnightUtc && afterMidnight.Start == midnightUtc
            && afterMidnight.End == midnightUtc.AddDays(1), "Live board day resets at Tehran midnight");
        var staleFetch = midnightUtc.AddSeconds(-1);
        Check(formatter.Format(new List<MatchDto>(), staleFetch, boardTime: staleFetch).Contains("جمعه ۶ آذر ۱۴۰۵")
            && formatter.Format(new List<MatchDto>(), staleFetch, boardTime: midnightUtc).Contains("شنبه ۷ آذر ۱۴۰۵"),
            "The Persian date follows the current Tehran day even when the last fetch was yesterday");
        Check(formatter.Format(new List<MatchDto>(), midnightUtc).Contains("فعلاً بازی زنده یا پیش رو برای نمایش نیست"),
            "Empty live board refers to today's fixtures");

        var finishedMatch = new MatchDto
        {
            CompetitionName = "بوندس‌لیگا",
            HomeTeam = "بایرن",
            AwayTeam = "دورتموند",
            HomeTeamFa = "بائرن ميونخ",
            AwayTeamFa = "بوروسيا دورتموند",
            StartUtc = DateTimeOffset.UtcNow.AddHours(-5),
            Status = MatchStatus.Finished,
            HomeScore = 3,
            AwayScore = 2
        };
        var finText = formatter.Format(new List<MatchDto> { finishedMatch }, DateTimeOffset.UtcNow);
        Check(finText.Contains("پایان"), "Finished status shown");
        finishedMatch.Statistics = new MatchStatistics(new(58, 42), null, null, null);
        finishedMatch.Events.Add(new MatchEvent { Type = "goal", Team = finishedMatch.HomeTeam, Minute = 72 });
        var archive = LiveBoardMessageFormatter.FormatYesterday(staleFetch.AddDays(-1), [finishedMatch]);
        var combined = formatter.Format([liveMatch], staleFetch, boardTime: staleFetch, yesterday: archive);
        var quote = combined.IndexOf("<blockquote expandable>", StringComparison.Ordinal);
        Check(quote > 0 && combined[..quote].Contains("بازی‌های زنده")
            && combined[quote..].Contains("پنجشنبه ۵ آذر ۱۴۰۵")
            && combined[quote..].Contains("⚽ گل")
            && combined[quote..].Contains("مالکیت توپ")
            && combined.EndsWith("</blockquote>", StringComparison.Ordinal),
            "Yesterday's local date, goals and final statistics render inside Telegram's expandable quote");
        var animated = formatter.Format([changedMatch], staleFetch, boardTime: staleFetch,
            yesterday: archive, animateEvents: true);
        const string goalVideo = "<video src=\"tg://video?id=goal\"></video>";
        var animation = animated.IndexOf(goalVideo, StringComparison.Ordinal);
        var archiveDetails = animated.IndexOf("<details><summary>🗓️ نتایج دیروز</summary>", StringComparison.Ordinal);
        Check(animation > animated.IndexOf("تازه", StringComparison.Ordinal) && archiveDetails > animation
            && !animated[archiveDetails..].Contains("<video")
            && !animated.Contains("tg://video?id=redcard")
            && !combined.Contains("<video"),
            "Goal animation follows the live event, with yesterday kept in a collapsed text section");
        var redCardMatch = new MatchDto
        {
            Status = MatchStatus.Live,
            RecentEvents = [new MatchEvent { Type = "redcard", Minute = 80 }]
        };
        Check(formatter.Format([redCardMatch], null, animateEvents: true).Contains("tg://video?id=redcard"),
            "A newly detected red card uses its own animation");

        // HTML encoding
        var encodedMatch = new MatchDto
        {
            CompetitionName = "تست &amp; <tag>",
            HomeTeam = "t<e>st",
            AwayTeam = "a&m",
            StartUtc = DateTimeOffset.UtcNow,
            Status = MatchStatus.Upcoming
        };
        var encText = formatter.Format(new List<MatchDto> { encodedMatch }, DateTimeOffset.UtcNow);
        Check(encText.Contains("&lt;"), "HTML encoded less-than");
        Check(encText.Contains("&amp;"), "HTML encoded ampersand");

        // 4096 char limit
        var manyMatches = new List<MatchDto>();
        for (int i = 0; i < 500; i++)
        {
            manyMatches.Add(new MatchDto
            {
                CompetitionName = "لیگ برتر ایران",
                HomeTeam = $"تیم{i}",
                AwayTeam = $"تیم{i + 1}",
                StartUtc = DateTimeOffset.UtcNow,
                Status = MatchStatus.Upcoming,
                HomeScore = 0,
                AwayScore = 0
            });
        }
        var longText = formatter.Format(manyMatches, DateTimeOffset.UtcNow);
        Check(longText.Length <= 4096, $"Long message truncated to {longText.Length} chars");
        Check(longText.Contains("بازی دیگر در این پیام جا نشد"), "Missed matches count shown");
        var longArchive = LiveBoardMessageFormatter.FormatYesterday(staleFetch.AddDays(-1),
            Enumerable.Repeat(finishedMatch, 500).ToList());
        var longCombined = formatter.Format(manyMatches, staleFetch, boardTime: staleFetch, yesterday: longArchive);
        Check(longCombined.Length <= 4096 && longCombined.EndsWith("</blockquote>", StringComparison.Ordinal)
            && longArchive.Contains("بازی دیگر در این پیام جا نشد"),
            "Today's fixtures and yesterday's expandable results share Telegram's message limit");

        var emptyArchive = LiveBoardMessageFormatter.FormatYesterday(staleFetch.AddDays(-1), []);
        Check(emptyArchive.Contains("فعلاً نتیجه‌ای برای نمایش نیست.") && !emptyArchive.Contains("⚠️"),
            "Empty archive uses a neutral message");

        Console.WriteLine("PASS: Live board formatter tests");
    }

    private static string GetRepositoryRoot()
    {
        return Path.Combine(AppContext.BaseDirectory, "../../../../../");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }

        Console.WriteLine("PASS: " + message);
    }

    private class CapturingHandler : HttpMessageHandler
    {
        public Uri RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<rss />")
            });
        }
    }
}
