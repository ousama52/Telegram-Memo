using SQLite;

namespace Telegram_Memo_App.Models;

[Table("MemoItem")]
public class MemoItem
{
    DateTime capturedAtUtc;
    DateTime? nextAttemptUtc;

    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    [NotNull]
    public string PhotoPath { get; set; } = string.Empty;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    // sqlite-net stores DateTime as ticks and reads it back as Unspecified, so force UTC.
    public DateTime CapturedAtUtc
    {
        get => capturedAtUtc;
        set => capturedAtUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public MemoStatus Status { get; set; } = MemoStatus.Pending;

    public bool PhotoSent { get; set; }

    public bool TextSent { get; set; }

    public bool LocationSent { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }

    public DateTime? NextAttemptUtc
    {
        get => nextAttemptUtc;
        set => nextAttemptUtc = value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    [Ignore]
    public bool HasLocation => Latitude is not null && Longitude is not null;
}
