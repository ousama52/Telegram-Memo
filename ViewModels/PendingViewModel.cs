using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Telegram_Memo_App.Models;
using Telegram_Memo_App.Services;

namespace Telegram_Memo_App.ViewModels;

public sealed class PendingItem
{
    public required string PhotoPath { get; init; }
    public required string Title { get; init; }
    public required string TimeText { get; init; }
    public required string StatusText { get; init; }
    public required Color StatusColor { get; init; }
    public string? Detail { get; init; }
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

public partial class PendingViewModel(QueueService queue, SettingsService settings, ChannelLauncher channel) : ObservableObject
{
    static readonly Color PendingColor = Color.FromArgb("#FFB020");
    static readonly Color SendingColor = Color.FromArgb("#2AABEE");
    static readonly Color FailedColor = Color.FromArgb("#FF5A52");

    public ObservableCollection<PendingItem> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string Hint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRetrying { get; set; }

    public void OnAppearing()
    {
        queue.QueueChanged += OnQueueChanged;
        _ = RefreshAsync();
    }

    public void OnDisappearing() => queue.QueueChanged -= OnQueueChanged;

    [RelayCommand]
    async Task RetryAsync()
    {
        if (IsRetrying)
            return;
        IsRetrying = true;
        try
        {
            await queue.RequeueAllAsync();
            if (!settings.IsConfigured)
            {
                await Toasts.ShowAsync("Telegram is not set up yet");
                return;
            }
            if (!QueueService.IsOnline)
            {
                await Toasts.ShowAsync("No connection. Will send when online");
                return;
            }
            await queue.ProcessAsync();
        }
        finally
        {
            IsRetrying = false;
        }
    }

    [RelayCommand]
    Task OpenChannelAsync() => channel.OpenAsync();

    [RelayCommand]
    Task BackAsync() => Shell.Current.GoToAsync("..");

    void OnQueueChanged() => _ = RefreshAsync();

    async Task RefreshAsync()
    {
        var rows = await queue.GetAllAsync();
        var items = rows.Select(ToItem).ToList();
        var hint = !settings.IsConfigured
            ? "Telegram is not set up yet. Memos stay on this phone until setup is finished."
            : !QueueService.IsOnline ? "No connection. Memos will be sent when you are back online." : string.Empty;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            IsEmpty = Items.Count == 0;
            Hint = hint;
        });
    }

    static PendingItem ToItem(MemoItem m)
    {
        var firstLine = m.Text.Split('\n', 2)[0].Trim();
        var (status, color) = m.Status switch
        {
            MemoStatus.Sending => ("Sending", SendingColor),
            MemoStatus.Failed => ("Failed", FailedColor),
            _ => ("Pending", PendingColor),
        };

        string? detail = m.Status switch
        {
            MemoStatus.Failed => m.LastError,
            MemoStatus.Pending when m.RetryCount > 0 => $"{m.LastError} · retry {m.RetryCount}",
            _ => null,
        };

        return new PendingItem
        {
            PhotoPath = m.PhotoPath,
            Title = string.IsNullOrEmpty(firstLine) ? "No note" : firstLine,
            TimeText = CaptionBuilder.DisplayTime(m.CapturedAtUtc),
            StatusText = status,
            StatusColor = color,
            Detail = detail,
        };
    }
}
