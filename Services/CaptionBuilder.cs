using System.Globalization;
using System.Text;

namespace Telegram_Memo_App.Services;

// Caption: the photo caption. ExtraMessages: the full note when it does not fit the caption.
public sealed record MessagePlan(string Caption, IReadOnlyList<string> ExtraMessages);

public static class CaptionBuilder
{
    public const int CaptionLimit = 1024;
    public const int MessageLimit = 4096;

    public static MessagePlan Build(string? note, DateTime capturedAtUtc, double? latitude, double? longitude)
    {
        var footer = BuildFooter(capturedAtUtc, latitude, longitude);
        var text = note?.Trim() ?? string.Empty;

        if (text.Length == 0)
            return new MessagePlan(footer, []);

        var full = text + "\n\n" + footer;
        if (full.Length <= CaptionLimit)
            return new MessagePlan(full, []);

        return new MessagePlan(footer, Split(text, MessageLimit));
    }

    public static string BuildFooter(DateTime capturedAtUtc, double? latitude, double? longitude)
    {
        var utc = DateTime.SpecifyKind(capturedAtUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
        var offset = TimeZoneInfo.Local.GetUtcOffset(utc);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var time = local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            + $" (UTC{sign}{offset.Duration():hh\\:mm})";

        var place = latitude is not null && longitude is not null
            ? "📍 " + MapLink(latitude.Value, longitude.Value)
            : "📍 Location unavailable";

        return $"🕒 {time}\n{place}";
    }

    public static string MapLink(double latitude, double longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"https://maps.google.com/?q={latitude:0.######},{longitude:0.######}");

    // Splits on a newline or space near the limit when possible.
    public static IReadOnlyList<string> Split(string text, int limit)
    {
        var parts = new List<string>();
        var rest = text;
        while (rest.Length > limit)
        {
            var cut = rest.LastIndexOf('\n', limit - 1);
            if (cut < limit / 2)
                cut = rest.LastIndexOf(' ', limit - 1);
            if (cut < limit / 2)
                cut = limit;
            // Do not cut a surrogate pair (emoji) in half.
            if (char.IsHighSurrogate(rest[cut - 1]))
                cut--;
            parts.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0)
            parts.Add(rest);
        return parts;
    }

    // "Oct 29, 2026, 14:32" in local time, for the info row and pending list.
    public static string DisplayTime(DateTime capturedAtUtc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(capturedAtUtc, DateTimeKind.Utc), TimeZoneInfo.Local)
            .ToString("MMM d, yyyy, HH:mm", CultureInfo.InvariantCulture);
}
