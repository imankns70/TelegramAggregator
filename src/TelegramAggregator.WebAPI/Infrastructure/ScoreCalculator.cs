using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.FootbalSettings;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class ScoreCalculator
    {
        private const int MaxScoreLimit = 200;

        public static int Calculate(NewsItem news, FootbalForiSettings settings)
        {
            var text = $"{news.Title} {news.ShortDescription} {news.FullDescription}".ToLowerInvariant();
            int score = 0;

            // ScoredItem-based dictionaries
            var scoredDictionaries = new List<Dictionary<string, List<ScoredItem>>>
            {
                settings.Conferences,
                settings.Teams,
                settings.Celebrities,
                settings.PerspolisPlayers,
                settings.EsteghlalPlayers,
                settings.Coaches
            };

            foreach (var dict in scoredDictionaries)
            {
                score += GetMaxScoreFromDictionary(dict, text);
            }

            // Commentators
            score += GetMaxScoreFromList(settings.Commentators, text);

            // Keywords
            score += ContainsAny(text, settings.AgencyKeywords) ? settings.Scoring.Weights.Agencies : 0;
            score += ContainsAny(text, settings.TransferKeywords) ? settings.Scoring.Weights.TransferNews : 0;
            score += ContainsAny(text, settings.SevereInjuryKeywords) ? settings.Scoring.Weights.SevereInjury : 0;
            score += ContainsAny(text, settings.ExclusiveTags) ? settings.Scoring.Weights.ExclusiveTag : 0;

            // Freshness
            var minutesOld = (DateTimeOffset.UtcNow - news.PubDate).TotalMinutes;
            if (minutesOld < 30)
                score += settings.Scoring.Weights.FreshnessBonus;
            else if (minutesOld < 60)
                score += settings.Scoring.Weights.FreshnessBonus / 2;

            // Derby matches
            if (IsDerbyMatch(text))
                score += settings.Scoring.Weights.DerbyMatch;

            return Math.Clamp(score, 0, MaxScoreLimit);
        }

        private static int GetMaxScoreFromDictionary(Dictionary<string, List<ScoredItem>> dict, string text)
        {
            int maxScore = 0;
            foreach (var list in dict.Values)
            {
                maxScore = Math.Max(maxScore, GetMaxScoreFromList(list, text));
            }
            return maxScore;
        }

        private static int GetMaxScoreFromList(IEnumerable<ScoredItem> items, string text)
        {
            return items
                .Where(i => text.Contains(i.Name.ToLowerInvariant()))
                .Max(i => (int?)i.Score) ?? 0;
        }

        private static bool ContainsAny(string text, IEnumerable<string>? keywords)
            => keywords?.Any(k => text.Contains(k.ToLowerInvariant())) == true;

        private static readonly string[] DerbyKeywords =
        {
            "دربی", "ال‌کلاسیکو", "ال کلاسیکو", "منچستر دربی", "دربی تهران",
            "دربی ۱۰", "پرسپولیس استقلال", "استقلال پرسپولیس",
            "رئال بارسا", "بارسا رئال", "بارسا لندن", "دربی منچستر"
        };

        private static bool IsDerbyMatch(string text) => ContainsAny(text, DerbyKeywords);
    }
}
