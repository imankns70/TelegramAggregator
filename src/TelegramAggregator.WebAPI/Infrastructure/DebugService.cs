using TelegramAggregator.Services;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    // یه فایل جدید بساز: Services/DebugService.cs
     public static class DebugService
    {
        // فقط در حافظه نگه می‌داره — وقتی برنامه ری‌استارت بشه پاک میشه (ولی برای دیباگ کافیه!)
        private static readonly List<DebugItem> Items = new();
        private static readonly object Lock = new();

     

        public static void AddProcessed(NewsItem item, bool isAccepted, string? rejectReason = null, string? publishStatus = null)
        {
            lock (Lock)
            {
                var debugItem = new DebugItem
                {
                    SourceName = item.SourceName,
                    Title = item.Title,
                    Link = item.Link,
                    PubDate = item.PubDate,
                    IsAccepted = isAccepted,
                    RejectReason = rejectReason, 
                    PublishStatus = publishStatus,
                    PreviewHtml = DebugPreviewHelper.GenerateTelegramPreview(item)
                };

                Items.Insert(0, debugItem);
                if (Items.Count > 100) Items.RemoveRange(100, Items.Count - 100);
            }
        }
        // توی DebugService اضافه کن
        public static NewsItem ToNewsItem(this DebugItem debugItem)
        {
            return new NewsItem
            {
                Title = debugItem.Title,
                FullDescription = debugItem.PreviewHtml?
                    .Replace("<br>", "\n")
                    .Replace("&lt;", "<")
                    .Replace("&gt;", ">")
                    .Replace("&amp;", "&")
                    // فقط متن بدون فوتر و دکمه
                    .Split("\n\n@FOOTBALFORI #iman")[0]
                    .Split("\n\n[")[0]
                    .Trim(),
                Link = debugItem.Link,
                SourceName = debugItem.SourceName,
                PubDate = debugItem.PubDate
            };
        }
        public static List<DebugItem> GetRecent() => Items.ToList();
        public static DebugItem? GetLatest() => Items.FirstOrDefault();
    }
}
