using SQLite;
using Telegram_Memo_App.Models;

namespace Telegram_Memo_App.Services;

public class QueueService
{
    static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    ];

    readonly SettingsService settings;
    readonly TelegramService telegram;
    readonly ImageService images;
    readonly SemaphoreSlim processLock = new(1, 1);
    readonly SemaphoreSlim initLock = new(1, 1);

    SQLiteAsyncConnection? db;
    CancellationTokenSource? wakeCts;

    public QueueService(SettingsService settings, TelegramService telegram, ImageService images)
    {
        this.settings = settings;
        this.telegram = telegram;
        this.images = images;
    }

    // Raised (on any thread) whenever rows are added, changed or removed.
    public event Action? QueueChanged;

    public static bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public async Task StartAsync()
    {
        await GetDbAsync();
        await settings.GetTokenAsync();
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
                _ = ProcessAsync();
        };
        _ = ProcessAsync();
    }

    async Task<SQLiteAsyncConnection> GetDbAsync()
    {
        if (db is not null)
            return db;

        await initLock.WaitAsync();
        try
        {
            if (db is null)
            {
                var conn = new SQLiteAsyncConnection(Path.Combine(FileSystem.AppDataDirectory, "memos.db3"));
                await conn.CreateTableAsync<MemoItem>();
                // Crash recovery: nothing can really be sending at startup.
                await conn.ExecuteAsync("UPDATE MemoItem SET Status = ? WHERE Status = ?", (int)MemoStatus.Pending, (int)MemoStatus.Sending);
                db = conn;
            }
            return db;
        }
        finally
        {
            initLock.Release();
        }
    }

    // Always the first step of Send: move the photo to app storage and insert the row.
    public async Task<int> SaveAsync(string capturePath, string text, double? latitude, double? longitude, DateTime capturedAtUtc)
    {
        var conn = await GetDbAsync();
        var item = new MemoItem
        {
            Text = text.Trim(),
            PhotoPath = images.MoveToPhotos(capturePath),
            Latitude = latitude,
            Longitude = longitude,
            CapturedAtUtc = capturedAtUtc,
            Status = MemoStatus.Pending,
        };
        await conn.InsertAsync(item);
        QueueChanged?.Invoke();
        return item.Id;
    }

    public async Task<List<MemoItem>> GetAllAsync()
    {
        var conn = await GetDbAsync();
        return await conn.Table<MemoItem>().OrderBy(m => m.Id).ToListAsync();
    }

    public async Task<int> GetUnsentCountAsync()
    {
        var conn = await GetDbAsync();
        return await conn.Table<MemoItem>().CountAsync();
    }

    public async Task<bool> ExistsAsync(int id)
    {
        var conn = await GetDbAsync();
        return await conn.FindAsync<MemoItem>(id) is not null;
    }

    // "Retry now" and Settings save: Failed -> Pending, and skip any backoff wait.
    public async Task RequeueAllAsync()
    {
        var conn = await GetDbAsync();
        await conn.ExecuteAsync(
            "UPDATE MemoItem SET Status = ?, NextAttemptUtc = NULL, RetryCount = 0, LastError = NULL WHERE Status = ?",
            (int)MemoStatus.Pending, (int)MemoStatus.Failed);
        await conn.ExecuteAsync("UPDATE MemoItem SET NextAttemptUtc = NULL WHERE Status = ?", (int)MemoStatus.Pending);
        QueueChanged?.Invoke();
    }

    // Sends pending memos one at a time, oldest first. Concurrent callers wait their turn,
    // so when this returns, a full pass has run after the call was made.
    public Task ProcessAsync() => Task.Run(async () =>
    {
        await processLock.WaitAsync();
        try
        {
            await ProcessCoreAsync();
        }
        catch (Exception)
        {
            // Never crash the app from background processing; the next trigger retries.
        }
        finally
        {
            processLock.Release();
        }
    });

    async Task ProcessCoreAsync()
    {
        var conn = await GetDbAsync();
        while (true)
        {
            var token = await settings.GetTokenAsync();
            var chatId = settings.ChatId;
            if (!settings.IsConfigured || token is null || !IsOnline)
                return;

            var item = await conn.Table<MemoItem>()
                .Where(m => m.Status == MemoStatus.Pending)
                .OrderBy(m => m.Id)
                .FirstOrDefaultAsync();
            if (item is null)
                return;

            // Keep channel order: do not skip ahead of an item that is waiting for its backoff.
            if (item.NextAttemptUtc is { } next && next > DateTime.UtcNow)
            {
                ScheduleWake(next);
                return;
            }

            item.Status = MemoStatus.Sending;
            await conn.UpdateAsync(item);
            QueueChanged?.Invoke();

            try
            {
                await SendItemAsync(conn, item, token, chatId);
                await conn.DeleteAsync(item);
                TryDelete(item.PhotoPath);
                QueueChanged?.Invoke();
            }
            catch (TelegramException ex) when (ex.Kind == TelegramErrorKind.Permanent)
            {
                item.Status = MemoStatus.Failed;
                item.LastError = ex.Message;
                item.NextAttemptUtc = null;
                await conn.UpdateAsync(item);
                QueueChanged?.Invoke();
                // Continue with the next item.
            }
            catch (Exception ex)
            {
                item.Status = MemoStatus.Pending;
                item.RetryCount++;
                var tex = ex as TelegramException;
                item.LastError = tex?.Message ?? "Could not send, will retry";
                var delay = tex?.Kind == TelegramErrorKind.RateLimited
                    ? TimeSpan.FromSeconds(tex.RetryAfterSeconds)
                    : Backoff[Math.Min(item.RetryCount, Backoff.Length) - 1];
                item.NextAttemptUtc = DateTime.UtcNow + delay;
                await conn.UpdateAsync(item);
                QueueChanged?.Invoke();
                ScheduleWake(item.NextAttemptUtc.Value);
                return;
            }
        }
    }

    // Each step is flagged right after Telegram confirms it, so a retry never repeats it.
    async Task SendItemAsync(SQLiteAsyncConnection conn, MemoItem item, string token, string chatId)
    {
        if (!File.Exists(item.PhotoPath))
            throw new TelegramException(TelegramErrorKind.Permanent, "Photo file is missing");

        var plan = CaptionBuilder.Build(item.Text, item.CapturedAtUtc, item.Latitude, item.Longitude);

        if (!item.PhotoSent)
        {
            RememberMessage(chatId, await telegram.SendPhotoAsync(token, chatId, item.PhotoPath, plan.Caption));
            item.PhotoSent = true;
            await conn.UpdateAsync(item);
        }

        if (!item.TextSent)
        {
            foreach (var part in plan.ExtraMessages)
                RememberMessage(chatId, await telegram.SendMessageAsync(token, chatId, part));
            item.TextSent = true;
            await conn.UpdateAsync(item);
        }

        if (!item.LocationSent && item.HasLocation)
        {
            await telegram.SendLocationAsync(token, chatId, item.Latitude!.Value, item.Longitude!.Value);
            item.LocationSent = true;
            await conn.UpdateAsync(item);
        }
    }

    // Lets "open channel" jump to the newest post (tg://privatepost).
    void RememberMessage(string chatId, int? messageId)
    {
        if (messageId is { } id)
            settings.SetLastMessageId(chatId, id);
    }

    void ScheduleWake(DateTime atUtc)
    {
        wakeCts?.Cancel();
        var cts = wakeCts = new CancellationTokenSource();
        var delay = atUtc - DateTime.UtcNow;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay + TimeSpan.FromMilliseconds(200), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            _ = ProcessAsync();
        });
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A leftover file is harmless.
        }
    }
}
