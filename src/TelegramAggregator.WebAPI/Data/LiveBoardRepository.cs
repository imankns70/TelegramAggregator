using Microsoft.EntityFrameworkCore;
using TelegramAggregator.WebAPI.Models.LiveBoard;
using System.Text.Json;

namespace TelegramAggregator.WebAPI.Data
{
    public class LiveBoardRepository : ILiveBoardRepository
    {
        private readonly TelegramAggregatorContext _db;

        public LiveBoardRepository(TelegramAggregatorContext db)
        {
            _db = db;
        }

        public Task<List<LiveBoardCompetition>> GetCompetitionsAsync(CancellationToken ct = default)
        {
            return _db.LiveBoardCompetitions.AsNoTracking()
                .OrderBy(competition => competition.DisplayOrder)
                .ThenBy(competition => competition.SourceCompetitionId)
                .ToListAsync(ct);
        }

        public async Task<List<MatchDto>> GetMatchesAsync(CancellationToken ct = default)
        {
            return await _db.LiveBoardMatches
                .AsNoTracking()
                .Select(e => new MatchDto
                {
                    SourceMatchId = e.SourceMatchId,
                    SourceCompetitionId = e.SourceCompetitionId,
                    CompetitionName = e.CompetitionName,
                    HomeTeam = e.HomeTeam,
                    AwayTeam = e.AwayTeam,
                    HomeTeamFa = e.HomeTeamFa,
                    AwayTeamFa = e.AwayTeamFa,
                    StartUtc = e.StartUtc,
                    Status = (MatchStatus)e.Status,
                    Minute = e.Minute,
                    HomeScore = e.HomeScore,
                    AwayScore = e.AwayScore,
                    HomeScoreEt = e.HomeScoreEt,
                    AwayScoreEt = e.AwayScoreEt,
                    HomeScorePen = e.HomeScorePen,
                    AwayScorePen = e.AwayScorePen,
                    Events = JsonSerializer.Deserialize<List<MatchEvent>>(e.EventsJson) ?? new(),
                    SourceUrl = e.SourceUrl,
                    DetailUrl = e.DetailUrl
                })
                .ToListAsync(ct);
        }

        public async Task UpsertMatchesAsync(List<MatchDto> matches, CancellationToken ct = default)
        {
            foreach (var match in matches)
            {
                var existing = await _db.LiveBoardMatches
                    .FirstOrDefaultAsync(m => m.SourceMatchId == match.SourceMatchId, ct);

                var eventsJson = JsonSerializer.Serialize(match.Events);

                if (existing == null)
                {
                    _db.LiveBoardMatches.Add(new LiveBoardMatchEntity
                    {
                        SourceMatchId = match.SourceMatchId,
                        SourceCompetitionId = match.SourceCompetitionId,
                        CompetitionName = match.CompetitionName,
                        HomeTeam = match.HomeTeam,
                        AwayTeam = match.AwayTeam,
                        HomeTeamFa = match.HomeTeamFa,
                        AwayTeamFa = match.AwayTeamFa,
                        StartUtc = match.StartUtc,
                        Status = (int)match.Status,
                        Minute = match.Minute,
                        HomeScore = match.HomeScore,
                        AwayScore = match.AwayScore,
                        HomeScoreEt = match.HomeScoreEt,
                        AwayScoreEt = match.AwayScoreEt,
                        HomeScorePen = match.HomeScorePen,
                        AwayScorePen = match.AwayScorePen,
                        EventsJson = eventsJson,
                        SourceUrl = match.SourceUrl,
                        DetailUrl = match.DetailUrl,
                        FetchedAt = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    existing.HomeTeam = match.HomeTeam;
                    existing.AwayTeam = match.AwayTeam;
                    existing.HomeTeamFa = match.HomeTeamFa;
                    existing.AwayTeamFa = match.AwayTeamFa;
                    existing.Status = (int)match.Status;
                    existing.Minute = match.Minute;
                    existing.HomeScore = match.HomeScore;
                    existing.AwayScore = match.AwayScore;
                    existing.HomeScoreEt = match.HomeScoreEt;
                    existing.AwayScoreEt = match.AwayScoreEt;
                    existing.HomeScorePen = match.HomeScorePen;
                    existing.AwayScorePen = match.AwayScorePen;
                    existing.EventsJson = eventsJson;
                    existing.SourceUrl = match.SourceUrl;
                    existing.DetailUrl = match.DetailUrl;
                    existing.FetchedAt = DateTimeOffset.UtcNow;
                }
            }
            await _db.SaveChangesAsync(ct);
        }

        public async Task<string?> GetStateAsync(string key, CancellationToken ct = default)
        {
            return await _db.LiveBoardStates
                .Where(s => s.Key == key)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(ct);
        }

        public async Task SetStateAsync(string key, string value, CancellationToken ct = default)
        {
            var existing = await _db.LiveBoardStates
                .FirstOrDefaultAsync(s => s.Key == key, ct);

            if (existing == null)
            {
                _db.LiveBoardStates.Add(new LiveBoardStateEntity
                {
                    Key = key,
                    Value = value,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
        }
    }
}
