using System.ServiceModel.Syndication;
using System.Xml;
using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public class RssParser
    {
        /// <summary>
        /// پارس کردن محتوای RSS و تبدیل به لیست NewsItem
        /// </summary>
        /// <param name="rssContent">محتوای RSS به صورت XML</param>
        /// <returns>لیست NewsItem</returns>
        public List<NewsItem> Parse(string rssContent)
        {
            var items = new List<NewsItem>();

            if (string.IsNullOrWhiteSpace(rssContent))
                return items;

            try
            {
                using var reader = XmlReader.Create(new System.IO.StringReader(rssContent));
                var feed = SyndicationFeed.Load(reader);

                if (feed == null) return items;

                foreach (var entry in feed.Items)
                {
                    var item = new NewsItem
                    {
                        Title = entry.Title?.Text ?? "",
                        ShortDescription = entry.Summary?.Text ?? "",
                        Link = entry.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                        PubDate = entry.PublishDate.DateTime,
                        SourceName = feed.Title?.Text ?? "RSS"
                    };

                    items.Add(item);
                }
            }
            catch
            {
                // در صورت خطا، لیست خالی بازگردانده می‌شود
            }

            return items;
        }
    }
}
