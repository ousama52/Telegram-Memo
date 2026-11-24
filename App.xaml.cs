using CommunityToolkit.Mvvm.Messaging;
using Telegram_Memo_App.Services;

namespace Telegram_Memo_App
{
    // Sent when the app comes back to the foreground.
    public sealed class AppResumedMessage;

    public partial class App : Application
    {
        readonly QueueService queue;
        readonly ImageService images;

        public App(QueueService queue, ImageService images)
        {
            InitializeComponent();
            // MAUI applies its own soft input mode (Pan) at runtime; Resize keeps Send above the keyboard.
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
                this.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
                Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
            this.queue = queue;
            this.images = images;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // Leftover captures from a Review that was never finished.
            images.ClearCaptures();
            _ = queue.StartAsync();

            var window = new Window(new AppShell());
            window.Resumed += (_, _) =>
            {
                _ = queue.ProcessAsync();
                WeakReferenceMessenger.Default.Send(new AppResumedMessage());
            };
            return window;
        }
    }
}
