using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TelegramAggregator.WebAPI.Data;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.LiveBoard;
using TelegramAggregator.WebAPI.Services.LiveBoard;

namespace TelegramAggregator.WebAPI.BackgroundServices;

public sealed class LiveBoardArchiveService(
    IServiceScopeFactory scopeFactory, IOptions<AppSettings> appSettings,
    ILogger<LiveBoardArchiveService> logger) : BackgroundService
{
    private readonly SemaphoreSlim _changed = new(0, 1);
    private readonly ConcurrentDictionary<string, List<MatchDto>> _results = new();
    private volatile ArchiveMessage _message;

    internal string GetText(DateTimeOffset now)
    {
        var message = _message;
        return message?.TodayStart == LiveBoardMatchDay.GetUtcBounds(now).Start ? message.Text : null;
    }

    internal Task WaitForChangeAsync(TimeSpan timeout, CancellationToken ct) => _changed.WaitAsync(timeout, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (appSettings.Value.LiveBoardSettings.ChannelId == 0)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var today = LiveBoardMatchDay.GetUtcBounds(DateTimeOffset.UtcNow);
            var yesterday = LiveBoardMatchDay.GetUtcBounds(today.Start.AddTicks(-1)).Start;
            if (_message?.TodayStart != today.Start)
                _results.Clear();

            bool complete;
            try
            {
                complete = await RefreshAsync(yesterday, today.Start, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                complete = false;
                logger.LogWarning(ex, "Could not refresh yesterday's live board results.");
            }

            var message = new ArchiveMessage(today.Start,
                LiveBoardMessageFormatter.FormatYesterday(yesterday, _results.Values.SelectMany(matches => matches).ToList()));
            if (_message != message)
            {
                _message = message;
                if (_changed.CurrentCount == 0)
                    _changed.Release();
            }

            var delay = TimeSpan.FromMinutes(complete ? 15 : 1);
            var untilMidnight = today.End - DateTimeOffset.UtcNow;
            if (untilMidnight > TimeSpan.Zero)
                await Task.Delay(untilMidnight < delay ? untilMidnight : delay, stoppingToken);
        }
    }

    private async Task<bool> RefreshAsync(DateTimeOffset yesterday, DateTimeOffset today, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ILiveBoardRepository>();
        var scraper = scope.ServiceProvider.GetRequiredService<IFotMobScraperService>();
        var competitions = await repo.GetCompetitionsAsync(ct);
        var activeIds = competitions.Select(competition => competition.SourceCompetitionId).ToHashSet();
        foreach (var id in _results.Keys.Where(id => !activeIds.Contains(id)))
            _results.TryRemove(id, out _);

        var failures = 0;
        await Parallel.ForEachAsync(competitions,
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, async (competition, token) =>
            {
                try
                {
                    var fixtures = await scraper.GetMatchesForDayAsync(competition, yesterday, token);
                    var matches = fixtures.Where(match => match.StartUtc >= yesterday && match.StartUtc < today).ToList();
                    foreach (var match in matches)
                        match.CompetitionDisplayOrder = competition.DisplayOrder;
                    _results[competition.SourceCompetitionId] = matches;
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    Interlocked.Increment(ref failures);
                    logger.LogWarning(ex, "Could not read yesterday's results for {Competition}.", competition.Name);
                }
            });

        return failures == 0;
    }

    private sealed record ArchiveMessage(DateTimeOffset TodayStart, string Text);
}
