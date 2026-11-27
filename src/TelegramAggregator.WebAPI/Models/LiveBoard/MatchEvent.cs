namespace TelegramAggregator.WebAPI.Models.LiveBoard
{
    public class MatchEvent
    {
        public string Type { get; set; } = "";
        public string Team { get; set; } = "";
        public string Player { get; set; } = "";
        public string OtherPlayer { get; set; } = "";
        public int Minute { get; set; }
        public string ExtraMinute { get; set; } // e.g. "3" for 90+3
        public string DisplayMinute => ExtraMinute != null ? $"{Minute}+{ExtraMinute}" : Minute.ToString();
    }
}
