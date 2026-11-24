using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace Telegram_Memo_App.Services;

// The photo that was just taken, handed from CameraPage to ReviewPage.
public sealed record CapturedPhoto(string PhotoPath, DateTime CapturedAtUtc, double? Latitude, double? Longitude);

public class CaptureSession
{
    public CapturedPhoto? Current { get; set; }
}

public static class Toasts
{
    static SnackbarOptions Options => new()
    {
        BackgroundColor = Color.FromArgb("#24272D"),
        TextColor = Color.FromArgb("#F2F1EE"),
        ActionButtonTextColor = Color.FromArgb("#2AABEE"),
        CornerRadius = new CornerRadius(16),
        Font = Microsoft.Maui.Font.SystemFontOfSize(15),
        ActionButtonFont = Microsoft.Maui.Font.SystemFontOfSize(15, FontWeight.Bold),
    };

    public static Task ShowSentAsync(Action open) => MainThread.InvokeOnMainThreadAsync(() =>
        Snackbar.Make("✓  Sent to Telegram", open, "Open", TimeSpan.FromSeconds(4), Options).Show());

    public static Task ShowSavedAsync() =>
        ShowAsync("☁  Saved. Will send when online");

    public static Task ShowAsync(string message) => MainThread.InvokeOnMainThreadAsync(() =>
        Snackbar.Make(message, null, "", TimeSpan.FromSeconds(3), Options).Show());
}

public static class Routes
{
    public const string Review = "review";
    public const string Settings = "settings";
    public const string Pending = "pending";
}
