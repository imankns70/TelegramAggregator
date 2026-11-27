using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramAggregator.WebAPI.Models.LiveBoard;

namespace TelegramAggregator.WebAPI.Services.LiveBoard
{
    public interface IFotMobScraperService
    {
        Task<List<MatchDto>> GetMatchesFromCompetitionPageAsync(string competitionUrl, string sourceCompetitionId, string competitionName, CancellationToken ct = default);
        Task<List<MatchDto>> GetFinishedMatchesAsync(LiveBoardCompetition competition, DateTimeOffset day, CancellationToken ct);
        Task<MatchDto?> GetMatchDetailAsync(string detailUrl, string? sourceMatchId, string sourceCompetitionId, CancellationToken ct = default);
    }
}
