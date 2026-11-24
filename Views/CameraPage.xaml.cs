using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Messaging;
using Telegram_Memo_App.Services;
using Telegram_Memo_App.ViewModels;

namespace Telegram_Memo_App.Views;

public partial class CameraPage : ContentPage
{
    readonly CameraViewModel vm;
    readonly ICameraProvider cameraProvider;
    readonly SettingsService settings;

    CameraView? camera;
    CameraInfo? rearCamera;
    CameraInfo? frontCamera;
    bool isVisible;
    bool previewRunning;
    bool locationStarted;

    public CameraPage(CameraViewModel vm, ICameraProvider cameraProvider, SettingsService settings)
    {
        InitializeComponent();
        BindingContext = this.vm = vm;
        this.cameraProvider = cameraProvider;
        this.settings = settings;
        vm.RetryCameraAsync = () => StartCameraAsync(requestPermission: true);

        // Coming back from Android settings does not raise OnAppearing.
        WeakReferenceMessenger.Default.Register<AppResumedMessage>(this, (_, _) =>
        {
            if (isVisible && camera is null)
                MainThread.BeginInvokeOnMainThread(() => _ = StartCameraAsync(requestPermission: false));
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        isVisible = true;
        vm.OnAppearing();
        await StartCameraAsync(requestPermission: true);

        if (isVisible)
            await vm.StartLocationAsync();
        locationStarted = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        isVisible = false;
        vm.OnDisappearing();

        // Release the camera while other pages are shown.
        if (camera is not null && previewRunning)
        {
            camera.StopCameraPreview();
            previewRunning = false;
        }
    }

    async Task StartCameraAsync(bool requestPermission)
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted && requestPermission)
                status = await Permissions.RequestAsync<Permissions.Camera>();

            if (status != PermissionStatus.Granted)
            {
                ShowFallback("Camera permission is needed", "Allow camera access to capture memos.");
                return;
            }

            await cameraProvider.RefreshAvailableCameras(CancellationToken.None);
            var cameras = cameraProvider.AvailableCameras ?? [];
            rearCamera = cameras.FirstOrDefault(c => c.Position == CameraPosition.Rear);
            frontCamera = cameras.FirstOrDefault(c => c.Position == CameraPosition.Front);
            var fallbackCamera = cameras.FirstOrDefault();

            if (fallbackCamera is null)
            {
                ShowFallback("No camera available", "This device does not report a usable camera.");
                return;
            }

            vm.ShowFallback = false;
            vm.CanSwitchCamera = rearCamera is not null && frontCamera is not null;

            if (!isVisible)
                return;

            if (camera is null)
            {
                var preferred = settings.LastCameraWasFront ? frontCamera : rearCamera;
                camera = new CameraView
                {
                    SelectedCamera = preferred ?? fallbackCamera,
                };
                // CameraX PreviewView letterboxes by default; fill the screen instead.
                camera.HandlerChanged += (_, _) =>
                {
                    if (camera.Handler?.PlatformView is AndroidX.Camera.View.PreviewView preview)
                        preview.SetScaleType(AndroidX.Camera.View.PreviewView.ScaleType.FillCenter!);
                };
                // Connecting the handler starts the preview.
                CameraHost.Children.Add(camera);
                previewRunning = true;
            }
            else if (!previewRunning)
            {
                await camera.StartCameraPreview(CancellationToken.None);
                previewRunning = true;
            }

            vm.IsCameraReady = true;
        }
        catch (Exception)
        {
            ShowFallback("Camera could not start", "Close other camera apps and try again.");
        }
    }

    void ShowFallback(string title, string text)
    {
        vm.FallbackTitle = title;
        vm.FallbackText = text;
        vm.ShowFallback = true;
        vm.IsCameraReady = false;
    }

    void OnSwitchCameraClicked(object? sender, EventArgs e)
    {
        if (camera is null || rearCamera is null || frontCamera is null || vm.IsCapturing)
            return;

        var toFront = camera.SelectedCamera?.Position != CameraPosition.Front;
        // Changing SelectedCamera restarts the preview with the new camera.
        camera.SelectedCamera = toFront ? frontCamera : rearCamera;
        settings.LastCameraWasFront = toFront;
    }

    async void OnShutterClicked(object? sender, EventArgs e)
    {
        if (camera is null || vm.IsCapturing || !previewRunning)
            return;

        vm.IsCapturing = true;
        ShutterButton.IsEnabled = false;
        try
        {
            // Time and location are taken at shutter time, not at send time.
            var capturedAtUtc = DateTime.UtcNow;
            var isFront = camera.SelectedCamera?.Position == CameraPosition.Front;
            var fixTask = locationStarted ? vm.GetShutterFixAsync() : Task.FromResult<LocationFix?>(null);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var stream = await camera.CaptureImage(cts.Token);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            await vm.OnPhotoCapturedAsync(ms.ToArray(), isFront, capturedAtUtc, fixTask);
        }
        catch (Exception)
        {
            await Toasts.ShowAsync("Could not take the photo. Try again.");
        }
        finally
        {
            vm.IsCapturing = false;
            ShutterButton.IsEnabled = true;
        }
    }
}
