using System.Globalization;

namespace JagexManager;

public sealed record PlayDay(DateOnly Date, int Seconds)
{
    public const int Kept = 7;

    private const int DaySeconds = 86400;

    public static List<PlayDay> Add(IEnumerable<PlayDay> days, DateTimeOffset from, DateTimeOffset to)
    {
        var totals = days.GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.Sum(d => d.Seconds));
        var start = from;
        while (start < to)
        {
            var day = start.ToLocalTime().Date;
            var next = day.AddDays(1);
            var midnight = new DateTimeOffset(next, TimeZoneInfo.Local.GetUtcOffset(next));
            var stop = to < midnight ? to : midnight;
            if (stop <= start)
                break;

            var date = DateOnly.FromDateTime(day);
            totals[date] = Math.Min(DaySeconds, totals.GetValueOrDefault(date) + (int)(stop - start).TotalSeconds);
            start = stop;
        }

        var oldest = DateOnly.FromDateTime(to.ToLocalTime().Date).AddDays(1 - Kept);
        return totals
            .Where(t => t.Key >= oldest && t.Value > 0)
            .OrderBy(t => t.Key)
            .Select(t => new PlayDay(t.Key, t.Value))
            .ToList();
    }

    public static TimeSpan Week(IEnumerable<PlayDay> days, DateOnly today)
    {
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var start = today.AddDays(-(((int)today.DayOfWeek - (int)first + 7) % 7));
        return TimeSpan.FromSeconds(days.Where(d => d.Date >= start && d.Date <= today).Sum(d => (long)d.Seconds));
    }

    public static string? Text(TimeSpan time) => time.TotalMinutes switch
    {
        < 1 => null,
        < 60 => $"{(int)time.TotalMinutes}m",
        _ when time.Minutes == 0 => $"{(int)time.TotalHours}h",
        _ => $"{(int)time.TotalHours}h {time.Minutes}m",
    };
}
