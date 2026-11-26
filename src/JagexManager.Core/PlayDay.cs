namespace JagexManager;

public sealed record PlayDay(DateOnly Date, int Seconds)
{
    public const int Kept = 7;

    private const int DaySeconds = 86400;

    public static List<PlayDay> Add(IEnumerable<PlayDay> days, DateTimeOffset from, DateTimeOffset to)
    {
        var totals = days.GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.Sum(d => d.Seconds));
        var start = from.ToLocalTime().DateTime;
        var end = to.ToLocalTime().DateTime;
        while (start < end)
        {
            var midnight = start.Date.AddDays(1);
            var stop = end < midnight ? end : midnight;
            var date = DateOnly.FromDateTime(start);
            totals[date] = Math.Min(DaySeconds, totals.GetValueOrDefault(date) + (int)(stop - start).TotalSeconds);
            start = stop;
        }

        var oldest = DateOnly.FromDateTime(end).AddDays(1 - Kept);
        return totals
            .Where(t => t.Key >= oldest && t.Value > 0)
            .OrderBy(t => t.Key)
            .Select(t => new PlayDay(t.Key, t.Value))
            .ToList();
    }

    public static TimeSpan Week(IEnumerable<PlayDay> days, DateOnly today) =>
        TimeSpan.FromSeconds(days.Where(d => d.Date > today.AddDays(-Kept) && d.Date <= today).Sum(d => (long)d.Seconds));

    public static string? Text(TimeSpan time) => time.TotalMinutes switch
    {
        < 1 => null,
        < 60 => $"{(int)time.TotalMinutes}m",
        _ when time.Minutes == 0 => $"{(int)time.TotalHours}h",
        _ => $"{(int)time.TotalHours}h {time.Minutes}m",
    };
}
