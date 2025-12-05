using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.FootbalSettings;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class ScoreCalculator
    {
        public static int Calculate(NewsItem news, FootbalForiSettings settings)
        {
            var text = $"{news.Title} {news.ShortDescription}".ToLowerInvariant();
                       

            int score = 0;

            score += GetMaxScore(settings.Teams, text);
            score += GetMaxScore(settings.Celebrities, text);
            score += GetMaxScore(settings.Coaches, text);
            score += GetCommentatorScore(text, settings.Commentators);

            if (ContainsAny(text, settings.TransferKeywords))
                score += settings.Scoring.Weights.TransferNews;

            if (ContainsAny(text, settings.SevereInjuryKeywords))
                score += settings.Scoring.Weights.SevereInjury;

            if (ContainsAny(text, settings.ExclusiveTags))
                score += settings.Scoring.Weights.ExclusiveTag;

            if ((DateTimeOffset.UtcNow - news.PubDate).TotalMinutes < 30)
                score += settings.Scoring.Weights.FreshnessBonus;

            return Math.Clamp(score, 0, 100);
        }

        private static int GetMaxScore(Dictionary<string, List<ScoredItem>> dict, string text)
        {
            int max = 0;
            foreach (var list in dict.Values)
                foreach (var item in list)
                    if (text.Contains(item.Name.ToLowerInvariant()))
                        max = Math.Max(max, item.Score);
            return max;
        }

        private static int GetCommentatorScore(string text, List<ScoredItem> commentators)
        {
            if (!text.Contains("گزارشگر") && !text.Contains("گزارش می‌کند")) return 0;
            return commentators
                .Where(c => text.Contains(c.Name.ToLowerInvariant()))
                .Max(c => (int?)c.Score) ?? 0;
        }

        private static bool ContainsAny(string text, IEnumerable<string> keywords)
            => keywords?.Any(k => text.Contains(k.ToLowerInvariant())) == true;
    }
}
