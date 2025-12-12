using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.FootbalSettings;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class ScoreCalculator
    {
        public static int Calculate(NewsItem news, FootbalForiSettings settings)
        {
            // همه متن‌ها رو با هم چک می‌کنیم (عنوان + شرح کوتاه + متن کامل)
            var text = $"{news.Title} {news.ShortDescription} {news.FullDescription}".ToLowerInvariant();

            int score = 0;

            // ۱. Conferences
            score += GetMaxScore(settings.Conferences, text);

            // ۱. تیم‌ها
            score += GetMaxScore(settings.Teams, text);

            // ۲. مربیان
            score += GetMaxScore(settings.Coaches, text);

            // ۳. سلبریتی‌های جهانی (مسی، رونالدو، هالند، عادل و …)
            score += GetMaxScore(settings.Celebrities, text);

            // ۴. بازیکنان پرسپولیس و استقلال — وزن جدا و منطقی
            score += GetMaxScore(settings.PerspolisPlayers, text);
            score += GetMaxScore(settings.EsteghlalPlayers, text);

            // ۵. گزارشگران (عادل، پیمان یوسفی و …)
            score += GetCommentatorScore(text, settings.Commentators);

            // ۶. نقل و انتقالات — یکی از مهم‌ترین موضوعات ایران!
            if (ContainsAny(text, settings.TransferKeywords))
                score += settings.Scoring.Weights.TransferNews;

            // ۷. مصدومیت سنگین
            if (ContainsAny(text, settings.SevereInjuryKeywords))
                score += settings.Scoring.Weights.SevereInjury;

            // ۸. خبر اختصاصی
            if (ContainsAny(text, settings.ExclusiveTags))
                score += settings.Scoring.Weights.ExclusiveTag;

            // ۹. تازگی خبر (حداکثر ۱۵ امتیاز)
            var minutesOld = (DateTimeOffset.UtcNow - news.PubDate).TotalMinutes;
            if (minutesOld < 30)
                score += settings.Scoring.Weights.FreshnessBonus;
            else if (minutesOld < 60)
                score += settings.Scoring.Weights.FreshnessBonus / 2; // +7.5 → 8

            // ۱۰. بازی زنده
            if (IsLiveMatch(text))
                score += settings.Scoring.Weights.LiveMatch;

            // ۱۱. دربی یا ال‌کلاسیکو
            if (IsDerbyMatch(text))
                score += settings.Scoring.Weights.DerbyMatch;

            // سقف امتیاز: ۲۰۰ (برای خبرهای خیلی داغ مثل دربی زنده با نقل رسمی!)
            return Math.Clamp(score, 0, 200);
        }

        private static int GetMaxScore(Dictionary<string, List<ScoredItem>> dict, string text)
        {
            int max = 0;
            foreach (var list in dict.Values)
            {
                foreach (var item in list)
                {
                    if (text.Contains(item.Name.ToLowerInvariant()))
                    {
                        max = Math.Max(max, item.Score);
                    }
                }
            }
            return max;
        }

        private static int GetCommentatorScore(string text, List<ScoredItem> commentators)
        {
            return commentators
                .Where(c => text.Contains(c.Name.ToLowerInvariant()))
                .Max(c => (int?)c.Score) ?? 0;
        }

        private static bool ContainsAny(string text, IEnumerable<string> keywords)
            => keywords?.Any(k => text.Contains(k.ToLowerInvariant())) == true;

        private static bool IsLiveMatch(string text)
        {
            return text.Contains("دقیقه") ||
                   text.Contains("گل به خودی") ||
                   text.Contains("پنالتی") ||
                   text.Contains("اخراج") ||
                   text.Contains("گلزنی") ||
                   text.Contains("نیمه اول") ||
                   text.Contains("نیمه دوم") ||
                   text.Contains("وقت اضافه") ||
                   text.Contains("در حال برگزاری");
        }

        private static bool IsDerbyMatch(string text)
        {
            return text.Contains("دربی") ||
                   text.Contains("ال‌کلاسیکو") ||
                   text.Contains("ال کلاسیکو") ||
                   text.Contains("منچستر دربی") ||
                   text.Contains("دربی تهران") ||
                   text.Contains("دربی ۱۰") ||
                   text.Contains("پرسپولیس استقلال") ||
                   text.Contains("استقلال پرسپولیس") ||
                   text.Contains("رئال بارسا") ||
                   text.Contains("بارسا رئال") ||
                   text.Contains("بارسا لندن") ||
                   text.Contains("دربی منچستر");
        }
    }
}