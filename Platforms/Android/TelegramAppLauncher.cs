using Android.Content;
using Telegram_Memo_App.Services;

namespace Telegram_Memo_App.Platforms.Android;

// Starts a tg:// link with a plain ACTION_VIEW intent. No package is set, so any
// Telegram client (official, Telegram X, ...) that handles tg:// can open it.
public class TelegramAppLauncher : ITelegramAppLauncher
{
    public bool TryOpen(Uri deepLink)
    {
        try
        {
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(deepLink.OriginalString));
            intent.AddFlags(ActivityFlags.NewTask);
            Platform.AppContext.StartActivity(intent);
            return true;
        }
        catch (ActivityNotFoundException)
        {
            // No Telegram app installed.
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
