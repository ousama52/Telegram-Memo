using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Telegram_Memo_App.Services;

public enum TelegramErrorKind
{
    Transient,
    RateLimited,
    Permanent,
}

// Message is always a readable, token-free text. Never wrap inner exceptions here,
// because HttpClient messages could contain the request URL (which includes the token).
public sealed class TelegramException(TelegramErrorKind kind, string message, int retryAfterSeconds = 0) : Exception(message)
{
    public TelegramErrorKind Kind { get; } = kind;

    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

public class TelegramService(HttpClient http)
{
    const string BaseUrl = "https://api.telegram.org/bot";

    // Returns result.message_id of the sent message (null if Telegram did not include it).
    public async Task<int?> SendMessageAsync(string token, string chatId, string text, CancellationToken ct = default)
    {
        using var doc = await PostAsync(token, "sendMessage", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
        }), ct);
        return GetMessageId(doc);
    }

    public async Task SendLocationAsync(string token, string chatId, double latitude, double longitude, CancellationToken ct = default)
    {
        using var doc = await PostAsync(token, "sendLocation", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["latitude"] = latitude.ToString(CultureInfo.InvariantCulture),
            ["longitude"] = longitude.ToString(CultureInfo.InvariantCulture),
        }), ct);
    }

    // Returns result.message_id of the sent photo (null if Telegram did not include it).
    public async Task<int?> SendPhotoAsync(string token, string chatId, string photoPath, string caption, CancellationToken ct = default)
    {
        await using var file = File.OpenRead(photoPath);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(chatId), "chat_id" },
            { new StringContent(caption), "caption" },
        };
        var photo = new StreamContent(file);
        photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(photo, "photo", Path.GetFileName(photoPath));
        using var doc = await PostAsync(token, "sendPhoto", form, ct);
        return GetMessageId(doc);
    }

    static int? GetMessageId(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("result", out var result)
        && result.ValueKind == JsonValueKind.Object
        && result.TryGetProperty("message_id", out var id)
        && id.TryGetInt32(out var value)
            ? value
            : null;

    // Returns the newest chat id found in getUpdates, or null when there are no usable updates.
    public async Task<string?> DetectChatIdAsync(string token, CancellationToken ct = default)
    {
        using var doc = await PostAsync(token, "getUpdates", new FormUrlEncodedContent(new Dictionary<string, string>()), ct);
        if (!doc.RootElement.TryGetProperty("result", out var updates) || updates.ValueKind != JsonValueKind.Array)
            return null;

        // Updates are oldest first, so walk backwards to find the newest.
        for (var i = updates.GetArrayLength() - 1; i >= 0; i--)
        {
            var update = updates[i];
            foreach (var key in new[] { "channel_post", "message", "my_chat_member" })
            {
                if (update.TryGetProperty(key, out var msg)
                    && msg.TryGetProperty("chat", out var chat)
                    && chat.TryGetProperty("id", out var id))
                {
                    return id.GetRawText();
                }
            }
        }
        return null;
    }

    async Task<JsonDocument> PostAsync(string token, string method, HttpContent content, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + token + "/" + method) { Content = content };
            response = await http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TelegramException(TelegramErrorKind.Transient, "Connection timed out");
        }
        catch (Exception)
        {
            throw new TelegramException(TelegramErrorKind.Transient, "No connection");
        }

        using (response)
        {
            JsonDocument? doc = null;
            try
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                doc = JsonDocument.Parse(body);
            }
            catch (Exception)
            {
                doc = null;
            }

            if (doc is not null
                && doc.RootElement.TryGetProperty("ok", out var ok)
                && ok.ValueKind == JsonValueKind.True)
            {
                return doc;
            }

            var code = (int)response.StatusCode;
            var description = string.Empty;
            var retryAfter = 0;
            if (doc is not null)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("error_code", out var ec) && ec.TryGetInt32(out var c))
                    code = c;
                if (root.TryGetProperty("description", out var d))
                    description = d.GetString() ?? string.Empty;
                if (root.TryGetProperty("parameters", out var p)
                    && p.TryGetProperty("retry_after", out var ra)
                    && ra.TryGetInt32(out var r))
                    retryAfter = r;
                doc.Dispose();
            }

            throw MapError(code, description, retryAfter);
        }
    }

    static TelegramException MapError(int code, string description, int retryAfter)
    {
        var lower = description.ToLowerInvariant();

        if (code == (int)HttpStatusCode.TooManyRequests)
            return new TelegramException(TelegramErrorKind.RateLimited, "Too many requests, waiting", Math.Max(retryAfter, 1));
        if (code >= 500 || code == 0)
            return new TelegramException(TelegramErrorKind.Transient, "Telegram server error");

        // Telegram answers 404 for a malformed token.
        if (code is 401 or 404)
            return new TelegramException(TelegramErrorKind.Permanent, "Invalid bot token");
        if (lower.Contains("chat not found"))
            return new TelegramException(TelegramErrorKind.Permanent, "Chat not found");
        if (code == 403 || lower.Contains("not enough rights") || lower.Contains("not a member"))
            return new TelegramException(TelegramErrorKind.Permanent, "Bot is not an admin of the channel");
        if (code == 400)
            return new TelegramException(TelegramErrorKind.Permanent, string.IsNullOrEmpty(description) ? "Request rejected by Telegram" : description);
        if (code == 409)
            return new TelegramException(TelegramErrorKind.Permanent, "The bot has a webhook set; remove it first");

        return new TelegramException(TelegramErrorKind.Transient, $"Telegram error {code}");
    }
}
