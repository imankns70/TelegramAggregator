namespace TelegramAggregator.WebAPI.Models.Entities
{
    public class NewsStatus
    {
        public int Id { get; set; }         
        public string Title { get; set; }          
        public ICollection<NewsHistory> Histories { get; set; } = new List<NewsHistory>();
        public ICollection<News> News { get; set; } = new List<News>();
    }
}
