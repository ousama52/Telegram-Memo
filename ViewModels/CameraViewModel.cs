using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Telegram_Memo_App.Services;

namespace Telegram_Memo_App.ViewModels;

// Camera hardware calls live in CameraPage code-behind (CameraView is a view);
// this view model owns the overlay state, location and navigation.
public partial class CameraViewModel : ObservableObject
{
    readonly LocationService location;
    readonly QueueService queue;
    readonly ImageService images;
    readonly CaptureSession session;
    readonly ChannelLauncher channel;

    public CameraViewModel(LocationService location, QueueService queue, ImageService images, CaptureSession session, ChannelLauncher channel)
    {
        this.location = location;
        this.queue = queue;
        this.images = images;
        this.session = session;
        this.channel = channel;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocating), nameof(IsLocationReady), nameof(IsLocationMissing), nameof(LocationText))]
    public partial LocationState LocationState { get; set; } = LocationState.Getting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending), nameof(PendingText))]
    public partial int PendingCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowControls))]
    public partial bool ShowFallback { get; set; }

    [ObservableProperty]
    public partial string FallbackTitle { get; set; } = "Camera permission is needed";

    [ObservableProperty]
    public partial string FallbackText { get; set; } = "Allow camera access to capture memos.";

    [ObservableProperty]
    public partial bool CanSwitchCamera { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowControls))]
    public partial bool IsCameraReady { get; set; }

    [ObservableProperty]
    public partial bool IsCapturing { get; set; }

    public bool IsLocating => LocationState == LocationState.Getting;

    public bool IsLocationReady => LocationState == LocationState.Ready;

    public bool IsLocationMissing => LocationState == LocationState.None;

    public string LocationText => LocationState switch
    {
        LocationState.Ready => "Location ready",
        LocationState.None => "No location",
        _ => "Getting location...",
    };

    public bool HasPending => PendingCount > 0;

    public string PendingText => $"{PendingCount} pending";

    public bool ShowControls => IsCameraReady && !ShowFallback;

    // Set by the page; opens the permission dialog again.
    public Func<Task>? RetryCameraAsync { get; set; }

    public void OnAppearing()
    {
        location.StateChanged += OnLocationChanged;
        queue.QueueChanged += OnQueueChanged;
        LocationState = location.State;
        _ = RefreshPendingAsync();
    }

    public void OnDisappearing()
    {
        location.StateChanged -= OnLocationChanged;
        queue.QueueChanged -= OnQueueChanged;
        location.Stop();
    }

    // Called after the camera permission flow so the two dialogs do not overlap.
    public Task StartLocationAsync() => location.StartAsync();

    public Task<LocationFix?> GetShutterFixAsync() => location.GetFixForShutterAsync();

    public async Task OnPhotoCapturedAsync(byte[] jpeg, bool isFrontCamera, DateTime capturedAtUtc, Task<LocationFix?> fixTask)
    {
        var pathTask = images.SaveCaptureAsync(jpeg, mirror: isFrontCamera);
        var fix = await fixTask;
        var path = await pathTask;
        session.Current = new CapturedPhoto(path, capturedAtUtc, fix?.Latitude, fix?.Longitude);
        await Shell.Current.GoToAsync(Routes.Review);
    }

    [RelayCommand]
    Task OpenSettingsAsync() => Shell.Current.GoToAsync(Routes.Settings);

    [RelayCommand]
    Task OpenPendingAsync() => Shell.Current.GoToAsync(Routes.Pending);

    [RelayCommand]
    Task OpenChannelAsync() => channel.OpenAsync();

    [RelayCommand]
    void OpenAppSettings() => AppInfo.Current.ShowSettingsUI();

    [RelayCommand]
    Task RetryPermissionAsync() => RetryCameraAsync?.Invoke() ?? Task.CompletedTask;

    void OnLocationChanged() =>
        MainThread.BeginInvokeOnMainThread(() => LocationState = location.State);

    void OnQueueChanged() => _ = RefreshPendingAsync();

    async Task RefreshPendingAsync()
    {
        var count = await queue.GetUnsentCountAsync();
        MainThread.BeginInvokeOnMainThread(() => PendingCount = count);
    }
}
