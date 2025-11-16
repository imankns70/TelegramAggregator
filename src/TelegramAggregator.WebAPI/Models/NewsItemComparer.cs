namespace TelegramAggregator.WebAPI.Models
{
    public class NewsItemComparer : IEqualityComparer<NewsItem>
    {
        public static readonly NewsItemComparer Instance = new();
        public bool Equals(NewsItem? x, NewsItem? y) =>
            x?.Link == y?.Link && x?.PubDate == y?.PubDate;
        public int GetHashCode(NewsItem obj) =>
            HashCode.Combine(obj.Link, obj.PubDate);
    }
}
