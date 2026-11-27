namespace TelegramAggregator.WebAPI.Models.LiveBoard
{
    public class LiveBoardCompetition
    {
        public string SourceCompetitionId { get; set; } = "";
        public string Name { get; set; } = "";
        public string SourceUrl { get; set; } = "";
        public int DisplayOrder { get; set; }
    }
}
