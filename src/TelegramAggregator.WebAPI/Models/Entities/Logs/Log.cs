using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramAggregator.WebAPI.Models.Entities.Logs
{
  
    public class Log
    {
        //[Key]
        //[Column("id")]
        public long Id { get; set; }  

        //[Column("message")]
        public string Message { get; set; } = string.Empty;

        //[Column("message_template")]
        public string MessageTemplate { get; set; }

        //[Column("level")]
        public string Level { get; set; } = string.Empty;

        //[Column("timestamp")]
        public DateTime TimeStamp { get; set; }

        //[Column("exception")]
        public string Exception { get; set; }

        //[Column("properties")]
        public string Properties { get; set; }  
    }
}