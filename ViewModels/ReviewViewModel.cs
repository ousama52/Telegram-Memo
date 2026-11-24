using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Telegram_Memo_App.Services;

namespace Telegram_Memo_App.ViewModels;

public partial class ReviewViewModel(
    CaptureSession session,
    QueueService queue,
    SettingsService settings,
    ChannelLauncher channel) : ObservableObject
{
    static readonly TimeSpan SendWait = TimeSpan.FromSeconds(60);

    CapturedPhoto? photo;

    [ObservableProperty]
    public partial string PhotoPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLocation { get; set; }

    [ObservableProperty]
    public partial string TimeText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CounterText), nameof(IsLongNote))]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSending { get; set; }

    // Preview sheet content
    [ObservableProperty]
    public partial string PreviewCaption { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> PreviewExtraMessages { get; set; } = [];

    [ObservableProperty]
    public partial string PreviewTime { get; set; } = string.Empty;

    public string LocationText => HasLocation ? "Location attached" : "No location";

    // Characters left for the note in the 1024 caption once the footer is added.
    int noteBudget = CaptionBuilder.CaptionLimit;

    public string CounterText => IsLongNote
        ? $"{Note.Length} · long note, sent as a separate message"
        : $"{Note.Length} / {noteBudget}";

    public bool IsLongNote => Note.Trim().Length > noteBudget;

    public void Load()
    {
        photo = session.Current;
        if (photo is null)
            return;
        PhotoPath = photo.PhotoPath;
        HasLocation = photo.Latitude is not null && photo.Longitude is not null;
        OnPropertyChanged(nameof(LocationText));
        TimeText = CaptionBuilder.DisplayTime(photo.CapturedAtUtc);
        noteBudget = CaptionBuilder.CaptionLimit - CaptionBuilder.BuildFooter(photo.CapturedAtUtc, photo.Latitude, photo.Longitude).Length - 2;
        OnPropertyChanged(nameof(CounterText));
        PreviewTime = TimeZoneInfo.ConvertTimeFromUtc(photo.CapturedAtUtc, TimeZoneInfo.Local).ToString("HH:mm");
    }

    public void BuildPreview()
    {
        if (photo is null)
            return;
        var plan = CaptionBuilder.Build(Note, photo.CapturedAtUtc, photo.Latitude, photo.Longitude);
        PreviewCaption = plan.Caption;
        PreviewExtraMessages = plan.ExtraMessages;
    }

    [RelayCommand]
    async Task RetakeAsync()
    {
        if (IsSending)
            return;
        DeleteCapture();
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    async Task SendAsync()
    {
        if (photo is null || IsSending)
            return;
        IsSending = true;

        int id;
        try
        {
            // One code path: always save to the queue first.
            id = await queue.SaveAsync(photo.PhotoPath, Note, photo.Latitude, photo.Longitude, photo.CapturedAtUtc);
            session.Current = null;
            photo = null;
        }
        catch (Exception)
        {
            IsSending = false;
            await Toasts.ShowAsync("Could not save the memo. Try again.");
            return;
        }

        await Shell.Current.GoToAsync("..");
        // Let the pop animation finish so the snackbar attaches to the camera page, not the page being removed.
        await Task.Delay(400);

        if (!settings.IsConfigured || !QueueService.IsOnline)
        {
            await Toasts.ShowSavedAsync();
            return;
        }

        var process = queue.ProcessAsync();
        var finished = await Task.WhenAny(process, Task.Delay(SendWait)) == process;
        if (finished && !await queue.ExistsAsync(id))
            await Toasts.ShowSentAsync(() => _ = channel.OpenAsync());
        else
            await Toasts.ShowSavedAsync();
    }

    // Hardware back on the Review page behaves like Retake.
    public void OnLeftWithoutSending()
    {
        if (!IsSending)
            DeleteCapture();
    }

    void DeleteCapture()
    {
        if (photo is null)
            return;
        try
        {
            File.Delete(photo.PhotoPath);
        }
        catch
        {
            // Cache file; cleaned up on next start anyway.
        }
        session.Current = null;
        photo = null;
    }
}
