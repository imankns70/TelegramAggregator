namespace TelegramAggregator.WebAPI.Models.Entities
{
    public class News
    {

        public long Id { get; set; } 
        public int? Score { get; set; }
        public string Title { get; set; }
        public string ShortDescription { get; set; }
        public string FullDescription { get; set; }
        public string Caption { get; set; }
        public string Link { get; set; }
        public DateTimeOffset InsertDate { get; set; }
        public DateTimeOffset PubDate { get; set; }
        public DateTimeOffset? SentDate { get; set; }
        public string ImageUrl { get; set; }
        public int NewsStatusId { get; set; }
        public int SportSourceId { get; set; }
        public SportSource SportSource { get; set; }
        public NewsStatus NewsStatus { get; set; }
        public ICollection<NewsHistory> Histories { get; set; } = new List<NewsHistory>();

    }
}
