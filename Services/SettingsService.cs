using System.Text.RegularExpressions;

namespace Telegram_Memo_App.Services;

public partial class SettingsService
{
    const string TokenKey = "bot_token";
    const string ChatIdKey = "chat_id";
    const string CameraKey = "last_camera_front";
    const string LastMessageIdKey = "last_message_id";
    const string LastMessageChatKey = "last_message_chat_id";

    string? cachedToken;
    bool tokenLoaded;

    public event Action? SettingsChanged;

    public string ChatId => Preferences.Default.Get(ChatIdKey, string.Empty);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(cachedToken) && IsValidChatId(ChatId);

    public bool LastCameraWasFront
    {
        get => Preferences.Default.Get(CameraKey, false);
        set => Preferences.Default.Set(CameraKey, value);
    }

    // Loads the token from SecureStorage once; afterwards IsConfigured is synchronous.
    public async Task<string?> GetTokenAsync()
    {
        if (!tokenLoaded)
        {
            try
            {
                cachedToken = await SecureStorage.Default.GetAsync(TokenKey);
            }
            catch
            {
                // SecureStorage can fail if the keystore was reset; treat as not configured.
                cachedToken = null;
            }
            tokenLoaded = true;
        }
        return cachedToken;
    }

    public async Task SaveAsync(string token, string chatId)
    {
        token = token.Trim();
        if (string.IsNullOrEmpty(token))
            SecureStorage.Default.Remove(TokenKey);
        else
            await SecureStorage.Default.SetAsync(TokenKey, token);

        cachedToken = string.IsNullOrEmpty(token) ? null : token;
        tokenLoaded = true;
        Preferences.Default.Set(ChatIdKey, chatId.Trim());
        SettingsChanged?.Invoke();
    }

    public static bool IsValidChatId(string? chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId))
            return false;
        chatId = chatId.Trim();
        return UsernameRegex().IsMatch(chatId) || NumericIdRegex().IsMatch(chatId);
    }

    // @name -> https://t.me/name, -100<digits> -> https://t.me/c/<digits>
    public static Uri? GetChannelUri(string? chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId))
            return null;
        chatId = chatId.Trim();
        if (UsernameRegex().IsMatch(chatId))
            return new Uri($"https://t.me/{chatId[1..]}");
        if (NumericIdRegex().IsMatch(chatId))
            return new Uri($"https://t.me/c/{chatId[4..]}");
        return null;
    }

    // Telegram app deep link:
    // @name -> tg://resolve?domain=name
    // -100<digits> -> tg://privatepost?channel=<digits>&post=<id> when a sent message id is known,
    //                 else tg://openmessage?chat_id=<digits>
    public static Uri? GetChannelDeepLink(string? chatId, int? lastMessageId)
    {
        if (string.IsNullOrWhiteSpace(chatId))
            return null;
        chatId = chatId.Trim();
        if (UsernameRegex().IsMatch(chatId))
            return new Uri($"tg://resolve?domain={chatId[1..]}");
        if (NumericIdRegex().IsMatch(chatId))
        {
            var digits = chatId[4..];
            return lastMessageId is { } post
                ? new Uri($"tg://privatepost?channel={digits}&post={post}")
                : new Uri($"tg://openmessage?chat_id={digits}");
        }
        return null;
    }

    // The id of the newest message this app sent successfully. It is stored together with
    // its chat ID, so changing the channel in Settings never opens a post from the old one.
    public void SetLastMessageId(string chatId, int messageId)
    {
        Preferences.Default.Set(LastMessageChatKey, chatId.Trim());
        Preferences.Default.Set(LastMessageIdKey, messageId);
    }

    public int? GetLastMessageId(string chatId)
    {
        if (Preferences.Default.Get(LastMessageChatKey, string.Empty) != chatId.Trim())
            return null;
        var id = Preferences.Default.Get(LastMessageIdKey, 0);
        return id > 0 ? id : null;
    }

    [GeneratedRegex(@"^@[A-Za-z0-9_]{4,}$")]
    private static partial Regex UsernameRegex();

    [GeneratedRegex(@"^-100\d+$")]
    private static partial Regex NumericIdRegex();
}
