using System.ComponentModel.DataAnnotations;
using YourNamespace.Entities;

namespace TelegramAggregator.WebAPI.Models
{
    public class SportSource
    {    
        public int Id { get; set; }       
        public string Title { get; set; }         
        public ICollection<News> News { get; set; } = new List<News>();

    }
}
