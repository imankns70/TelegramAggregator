using Microsoft.EntityFrameworkCore;
using TelegramAggregator.WebAPI.Models.LiveBoard;

namespace TelegramAggregator.WebAPI.Data
{
    public interface ILiveBoardRepository
    {
        Task<List<LiveBoardCompetition>> GetCompetitionsAsync(CancellationToken ct = default);
        Task<List<MatchDto>> GetMatchesAsync(CancellationToken ct = default);
        Task UpsertMatchesAsync(List<MatchDto> matches, CancellationToken ct = default);
        Task<string?> GetStateAsync(string key, CancellationToken ct = default);
        Task SetStateAsync(string key, string value, CancellationToken ct = default);
    }
}
