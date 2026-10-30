using System.Globalization;

namespace NotionTimesheet.Services;

/// <summary>
/// Time helpers. Every timestamp written to Notion carries an explicit +07:00 offset
/// (Asia/Jakarta), regardless of what the machine timezone happens to be.
/// </summary>
internal static class Clock
{
    public static readonly TimeSpan WibOffset = TimeSpan.FromHours(7);

    /// <summary>Now, normalised to +07:00.</summary>
    public static DateTimeOffset Now() => DateTimeOffset.Now.ToOffset(WibOffset);

    /// <summary>2026-10-30T14:35:00+07:00</summary>
    public static string Stamp(DateTimeOffset value) =>
        value.ToOffset(WibOffset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>2026-10-30</summary>
    public static string Day(DateTimeOffset value) =>
        value.ToOffset(WibOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>October 2026</summary>
    public static string MonthName(DateTimeOffset value) =>
        value.ToOffset(WibOffset).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>14:35</summary>
    public static string Time(DateTimeOffset value) =>
        value.ToOffset(WibOffset).ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Friendly(string? notionTimestamp)
    {
        if (string.IsNullOrWhiteSpace(notionTimestamp))
        {
            return "no start time";
        }

        return DateTimeOffset.TryParse(notionTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? $"{parsed.ToOffset(WibOffset).ToString("ddd HH:mm", CultureInfo.InvariantCulture)} WIB"
            : notionTimestamp;
    }
}

