using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using TelegramAggregator.WebAPI.Data;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.LiveBoard;
using TelegramAggregator.WebAPI.Services.LiveBoard;

namespace TelegramAggregator.WebAPI.BackgroundServices;

public class LiveBoardBackgroundService(
    ILogger<LiveBoardBackgroundService> logger,
    IServiceScopeFactory scopeFactory,
    IOptions<AppSettings> appSettings,
    LiveBoardArchiveService archive) : BackgroundService
{
    private readonly Dictionary<string, CompetitionSnapshot> _snapshots = new();
    private int? _pinnedMessageId;
    private string? _lastPublishedFingerprint;
    private DateTimeOffset _editAllowedAfter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = appSettings.Value.LiveBoardSettings;
        if (settings.ChannelId == 0)
        {
            logger.LogError("Live board requires a channel ID.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var started = DateTimeOffset.UtcNow;
            try
            {
                await RunCycleAsync(settings, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Live board cycle failed.");
            }

            var delay = GetNextDelay(settings, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow - started);
            await archive.WaitForChangeAsync(delay, stoppingToken);
        }
    }

    private async Task RunCycleAsync(LiveBoardSettings settings, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ILiveBoardRepository>();
        var scraper = scope.ServiceProvider.GetRequiredService<IFotMobScraperService>();
        var formatter = scope.ServiceProvider.GetRequiredService<ILiveBoardMessageFormatter>();
        var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        var messageId = _pinnedMessageId ??= await EnsureBoardMessagePinnedAsync(scope.ServiceProvider, repo, bot, settings, ct);

        var competitions = await repo.GetCompetitionsAsync(ct);
        var configuredIds = competitions.Select(competition => competition.SourceCompetitionId).ToHashSet();
        foreach (var removedId in _snapshots.Keys.Where(id => !configuredIds.Contains(id)).ToList())
            _snapshots.Remove(removedId);

        foreach (var competition in competitions)
        {
            var id = competition.SourceCompetitionId;
            if (_snapshots.ContainsKey(id))
                continue;
            var stored = await repo.GetStateAsync($"snapshot:{id}", ct);
            if (stored != null && JsonSerializer.Deserialize<CompetitionSnapshot>(stored) is { } snapshot)
                _snapshots[id] = snapshot;
        }

        var fresh = new ConcurrentDictionary<string, CompetitionSnapshot>();
        await Parallel.ForEachAsync(competitions, new ParallelOptions
        {
            MaxDegreeOfParallelism = 3,
            CancellationToken = ct
        }, async (competition, token) =>
        {
            try
            {
                var matches = await scraper.GetMatchesFromCompetitionPageAsync(competition.SourceUrl,
                    competition.SourceCompetitionId, competition.Name, token);
                fresh[competition.SourceCompetitionId] = new CompetitionSnapshot(DateTimeOffset.UtcNow, matches);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not refresh {Competition}.", competition.Name);
            }
        });

        foreach (var (id, snapshot) in fresh)
        {
            if (_snapshots.TryGetValue(id, out var previous))
            {
                var previousMatches = previous.Matches.ToDictionary(match => match.SourceMatchId);
                foreach (var match in snapshot.Matches)
                {
                    previousMatches.TryGetValue(match.SourceMatchId, out var priorMatch);
                    LiveBoardUpdates.UpdateHighlights(match, priorMatch, snapshot.FetchedAt);
                }
            }

            _snapshots[id] = snapshot;
            await repo.SetStateAsync($"snapshot:{id}", JsonSerializer.Serialize(snapshot), ct);
        }

        foreach (var competition in competitions)
        {
            if (!_snapshots.TryGetValue(competition.SourceCompetitionId, out var snapshot))
                continue;
            foreach (var match in snapshot.Matches)
            {
                match.CompetitionName = competition.Name;
                match.CompetitionDisplayOrder = competition.DisplayOrder;
            }
        }
        var lastFetch = _snapshots.Values.Select(snapshot => (DateTimeOffset?)snapshot.FetchedAt).Max();
        var failedCompetitions = competitions.Count - fresh.Count;
        var boardTime = DateTimeOffset.UtcNow;
        var yesterday = archive.GetText(boardTime);
        var matchesToShow = SelectMatches(boardTime);
        var animate = matchesToShow.Any(match => match.Status.IsInProgress() &&
            match.RecentEvents.Any(e => LiveBoardMessageFormatter.IsAnimatedEvent(e.Type)));
        var content = formatter.Format(matchesToShow, lastFetch, settings.LiveRefreshSeconds, boardTime, yesterday, animate,
            failedCompetitions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var fingerprintKey = $"published:{settings.ChannelId}:{messageId}";
        _lastPublishedFingerprint ??= await repo.GetStateAsync(fingerprintKey, ct);
        if (fingerprint == _lastPublishedFingerprint || DateTimeOffset.UtcNow < _editAllowedAfter)
            return;

        try
        {
            var published = false;
            if (animate)
            {
                var rich = content;
                if (EditAnimatedBoardRequest.HasAnimation(rich))
                {
                    try
                    {
                        await RetryEditAsync(
                            () => bot.SendRequest(new EditAnimatedBoardRequest(settings.ChannelId, messageId, rich), ct), ct);
                        published = true;
                    }
                    catch (ApiRequestException ex) when ((ex.ErrorCode is 400 or 403) &&
                        !ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogWarning("Telegram declined the live board animation. Using the text update. {Reason}", ex.Message);
                    }
                }
            }
            if (!published)
            {
                var plain = animate
                    ? formatter.Format(matchesToShow, lastFetch, settings.LiveRefreshSeconds, boardTime, yesterday,
                        failedCompetitions: failedCompetitions)
                    : content;
                await RetryEditAsync(() => bot.EditMessageText(settings.ChannelId, messageId, plain,
                    ParseMode.Html, cancellationToken: ct), ct);
            }
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 429)
        {
            _editAllowedAfter = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, ex.Parameters?.RetryAfter ?? 30));
            logger.LogWarning("Telegram rate limited live board edits until {RetryAt}.", _editAllowedAfter);
            return;
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400 &&
            ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            // The pinned message already contains this text, for example after a restart.
        }
        catch (RequestException ex) when (!ct.IsCancellationRequested && IsTransientEditFailure(ex))
        {
            logger.LogWarning("Telegram connection interrupted during live board edit. The existing message remains published.");
            return;
        }
        _lastPublishedFingerprint = fingerprint;
        await repo.SetStateAsync(fingerprintKey, fingerprint, ct);
    }

    internal static async Task RetryEditAsync(Func<Task> edit, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await edit();
                return;
            }
            catch (RequestException ex) when (attempt < 2 && !ct.IsCancellationRequested && IsTransientEditFailure(ex))
            {
                // Updating a known message is safe to retry even if Telegram already received the edit.
                await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 1 : 3), ct);
            }
        }
    }

    private static bool IsTransientEditFailure(RequestException exception)
    {
        for (var cause = exception.InnerException; cause != null; cause = cause.InnerException)
        {
            if (cause is SocketException { SocketErrorCode: SocketError.ConnectionReset
                or SocketError.NetworkReset or SocketError.ConnectionAborted or SocketError.TimedOut })
                return true;
        }
        return false;
    }

    private List<MatchDto> SelectMatches(DateTimeOffset now)
    {
        var matches = GetTodayMatches(LiveBoardMatchDay.GetUtcBounds(now)).ToList();
        foreach (var match in matches.Where(match => match.RecentEventsUntil <= now))
        {
            match.RecentEvents.Clear();
            match.RecentEventsUntil = null;
        }
        return matches;
    }

    private TimeSpan GetNextDelay(LiveBoardSettings settings, DateTimeOffset now, TimeSpan elapsed)
    {
        var day = LiveBoardMatchDay.GetUtcBounds(now);
        var matches = GetTodayMatches(day).ToList();
        var liveInterval = TimeSpan.FromSeconds(Math.Max(1, settings.LiveRefreshSeconds));
        var nearKickoff = matches.Any(match => match.Status == MatchStatus.Upcoming &&
            match.StartUtc >= now.AddMinutes(-5) && match.StartUtc <= now.AddMinutes(1));
        var interval = matches.Any(match => match.Status.IsInProgress()) || nearKickoff
            ? liveInterval - elapsed
            : TimeSpan.FromMinutes(Math.Max(1, settings.OfflineRefreshMinutes));
        var nextStart = matches.Where(match => match.Status == MatchStatus.Upcoming && match.StartUtc > now.AddMinutes(1))
            .Select(match => (TimeSpan?)(match.StartUtc - now - TimeSpan.FromMinutes(1))).Min();
        if (nextStart is { } untilStart && untilStart < interval)
            interval = untilStart;
        var untilMidnight = day.End - now;
        if (untilMidnight < interval)
            interval = untilMidnight;
        return interval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : interval;
    }

    private IEnumerable<MatchDto> GetTodayMatches((DateTimeOffset Start, DateTimeOffset End) day) =>
        _snapshots.Values.SelectMany(snapshot => snapshot.Matches)
            .Where(match => match.StartUtc >= day.Start && match.StartUtc < day.End);

    private async Task<int> EnsureBoardMessagePinnedAsync(
        IServiceProvider services, ILiveBoardRepository repo, ITelegramBotClient bot,
        LiveBoardSettings settings, CancellationToken ct)
    {
        var messageId = await GetMessageIdAsync(settings, repo, ct)
            ?? await GetOrCreateBoardMessageAsync(services, repo, bot, settings, ct);
        var pinnedKey = $"pinned:{settings.ChannelId}:{messageId}";
        if (await repo.GetStateAsync(pinnedKey, ct) == "true")
            return messageId;

        try
        {
            await bot.PinChatMessage(settings.ChannelId, messageId, disableNotification: true, cancellationToken: ct);
        }
        catch (ApiRequestException ex)
        {
            throw new InvalidOperationException(
                $"Could not pin live board message {messageId}. Check the bot's channel administrator rights.", ex);
        }

        await repo.SetStateAsync(pinnedKey, "true", ct);
        logger.LogInformation("Pinned live board message {MessageId} in channel {ChannelId}.", messageId, settings.ChannelId);
        return messageId;
    }

    private static async Task<int> GetOrCreateBoardMessageAsync(
        IServiceProvider services, ILiveBoardRepository repo, ITelegramBotClient bot, LiveBoardSettings settings, CancellationToken ct)
    {
        var db = services.GetRequiredService<TelegramAggregatorContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({settings.ChannelId})", ct);
        if (await GetMessageIdAsync(settings, repo, ct) is int existingId)
            return existingId;

        var sent = await bot.SendMessage(settings.ChannelId,
            LiveBoardMessageFormatter.FormatHeading(DateTimeOffset.UtcNow, settings.LiveRefreshSeconds),
            ParseMode.Html, cancellationToken: ct);
        await repo.SetStateAsync(MessageKey(settings.ChannelId), sent.MessageId.ToString(), CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return sent.MessageId;
    }

    private static async Task<int?> GetMessageIdAsync(LiveBoardSettings settings, ILiveBoardRepository repo, CancellationToken ct)
    {
        if (settings.MessageId > 0)
            return settings.MessageId;

        return int.TryParse(await repo.GetStateAsync(MessageKey(settings.ChannelId), ct), out var id) && id > 0
            ? id : null;
    }

    private static string MessageKey(long channelId) => $"messageId:{channelId}";

    private sealed record CompetitionSnapshot(DateTimeOffset FetchedAt, List<MatchDto> Matches);
}
