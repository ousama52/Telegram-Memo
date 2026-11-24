using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Telegram_Memo_App.Services;
using Telegram_Memo_App.ViewModels;
using Telegram_Memo_App.Views;

namespace Telegram_Memo_App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitCamera()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            RemoveAndroidUnderlines();

            // One shared HttpClient. Photo uploads on slow networks can take a while.
            builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(60) });

            builder.Services.AddSingleton<SettingsService>();
            builder.Services.AddSingleton<TelegramService>();
            builder.Services.AddSingleton<ImageService>();
            builder.Services.AddSingleton<LocationService>();
            builder.Services.AddSingleton<QueueService>();
            builder.Services.AddSingleton<CaptureSession>();
            builder.Services.AddSingleton<ChannelLauncher>();
            builder.Services.AddSingleton<ITelegramAppLauncher, Platforms.Android.TelegramAppLauncher>();

            builder.Services.AddTransient<CameraViewModel>();
            builder.Services.AddTransient<ReviewViewModel>();
            builder.Services.AddTransient<PendingViewModel>();
            builder.Services.AddTransient<SettingsViewModel>();

            builder.Services.AddTransient<CameraPage>();
            builder.Services.AddTransient<ReviewPage>();
            builder.Services.AddTransient<PendingPage>();
            builder.Services.AddTransient<SettingsPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }

        // Android draws an underline under Entry/Editor; the design uses rounded boxes instead.
        static void RemoveAndroidUnderlines()
        {
            Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, _) =>
                handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
            Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("NoUnderline", (handler, _) =>
                handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
        }
    }
}
