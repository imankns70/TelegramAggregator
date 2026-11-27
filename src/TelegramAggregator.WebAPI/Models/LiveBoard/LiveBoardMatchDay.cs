namespace TelegramAggregator.WebAPI.Models.LiveBoard;

internal static class LiveBoardMatchDay
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public static DateTimeOffset ToTehran(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Tehran);

    public static (DateTimeOffset Start, DateTimeOffset End) GetUtcBounds(DateTimeOffset now)
    {
        var localMidnight = ToTehran(now).Date;
        return (
            TimeZoneInfo.ConvertTimeToUtc(localMidnight, Tehran),
            TimeZoneInfo.ConvertTimeToUtc(localMidnight.AddDays(1), Tehran));
    }
}
