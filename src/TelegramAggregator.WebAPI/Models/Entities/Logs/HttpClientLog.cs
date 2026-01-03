using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramAggregator.WebAPI.Models.Entities
{
    [Table("log_http_client", Schema = "public")]
    public class HttpClientLog
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("message")]
        public string Message { get; set; } = string.Empty;

        [Column("level")]
        public string Level { get; set; } = string.Empty;

        [Column("timestamp")]
        public DateTime TimeStamp { get; set; }

        [Column("exception")]
        public string Exception { get; set; }

        [Column("properties")]
        public string Properties { get; set; }  

        [Column("http_client")]
        public string HttpClient { get; set; }

        [Column("requested_url")]
        public string RequestedUrl { get; set; }

        [Column("input_data")]
        public string InputData { get; set; }  
    }
}