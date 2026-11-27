using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramAggregator.WebAPI.Models.LiveBoard
{
    [Table("LiveBoardState")]
    public class LiveBoardStateEntity
    {
        public int Id { get; set; }
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
