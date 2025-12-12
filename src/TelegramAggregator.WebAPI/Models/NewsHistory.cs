using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TelegramAggregator.WebAPI.Models;

namespace YourNamespace.Entities // اسم namespace خودت رو بذار
{
     
    public class NewsHistory
    {     
        public long Id { get; set; }      
        public long NewsId { get; set; }  
        public int NewsStatusId { get; set; }     
        public string Reason { get; set; }
        public DateTime InsertDate { get; set; } 
        public NewsStatus NewsStatus { get; set; }
        public News News { get; set; }

    }
}