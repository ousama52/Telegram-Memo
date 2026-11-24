namespace Telegram_Memo_App.Services;

// Platform hook: opens a tg:// deep link in a Telegram app. Returns false when no app handles it.
public interface ITelegramAppLauncher
{
    bool TryOpen(Uri deepLink);
}

// The one shared helper behind every "open the channel" action
// (camera paper-plane, toast "Open", Settings button, Pending empty state).
public class ChannelLauncher(SettingsService settings, ITelegramAppLauncher appLauncher)
{
    // chatIdOverride: Settings passes the value currently typed in the field.
    public async Task OpenAsync(string? chatIdOverride = null)
    {
        var chatId = (chatIdOverride ?? settings.ChatId).Trim();
        var webUri = SettingsService.GetChannelUri(chatId);
        if (webUri is null)
        {
            await Shell.Current.GoToAsync(Routes.Settings);
            return;
        }

        // 1. Telegram app via tg:// deep link.
        var deepLink = SettingsService.GetChannelDeepLink(chatId, settings.GetLastMessageId(chatId));
        if (deepLink is not null && appLauncher.TryOpen(deepLink))
            return;

        // 2. https://t.me link (browser, or Telegram if it claims t.me links).
        try
        {
            if (await Launcher.Default.OpenAsync(webUri))
                return;
        }
        catch
        {
            // Fall through to the message below.
        }

        // 3. Nothing could open it.
        await Toasts.ShowAsync("Telegram app not found");
    }
}
