using System.Text.Json.Serialization;

namespace TelegramAggregator.WebAPI.Models.FootbalSettings
{
    public class ScoredItem
    {
        public string Name { get; set; } = string.Empty;
        public int Score { get; set; }

        // برای جستجوی سریع‌تر (اختیاری)
        [JsonIgnore]
        public string NameLower => Name.ToLowerInvariant();
    }
}
