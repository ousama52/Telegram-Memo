using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Telegram_Memo_App.Services;

namespace Telegram_Memo_App.ViewModels;

public enum TestState
{
    None,
    Success,
    Error,
}

public partial class SettingsViewModel(
    SettingsService settings,
    TelegramService telegram,
    QueueService queue,
    ChannelLauncher channel) : ObservableObject
{
    [ObservableProperty]
    public partial string BotToken { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenChannel))]
    public partial string ChatId { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenEyeIcon))]
    public partial bool IsTokenHidden { get; set; } = true;

    [ObservableProperty]
    public partial bool IsConfigured { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTestSuccess), nameof(IsTestError))]
    public partial TestState TestState { get; set; }

    [ObservableProperty]
    public partial string TestMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChatIdMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ValidationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string TokenEyeIcon => IsTokenHidden ? "ic_eye.png" : "ic_eye_off.png";

    public bool CanOpenChannel => !string.IsNullOrWhiteSpace(ChatId);

    public bool IsTestSuccess => TestState == TestState.Success;

    public bool IsTestError => TestState == TestState.Error;

    public async Task LoadAsync()
    {
        BotToken = await settings.GetTokenAsync() ?? string.Empty;
        ChatId = settings.ChatId;
        IsConfigured = settings.IsConfigured;
    }

    [RelayCommand]
    void ToggleToken() => IsTokenHidden = !IsTokenHidden;

    [RelayCommand]
    async Task DetectChatIdAsync()
    {
        ChatIdMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(BotToken))
        {
            ChatIdMessage = "Enter the bot token first.";
            return;
        }

        IsBusy = true;
        try
        {
            var id = await telegram.DetectChatIdAsync(BotToken.Trim());
            if (id is null)
            {
                ChatIdMessage = "Post any message in the channel first, then try again.";
            }
            else
            {
                ChatId = id;
                ChatIdMessage = "Chat ID detected.";
            }
        }
        catch (TelegramException ex)
        {
            ChatIdMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    async Task SendTestAsync()
    {
        TestState = TestState.None;
        if (string.IsNullOrWhiteSpace(BotToken) || string.IsNullOrWhiteSpace(ChatId))
        {
            ShowTestError("Enter the bot token and chat ID first");
            return;
        }

        IsBusy = true;
        try
        {
            var messageId = await telegram.SendMessageAsync(BotToken.Trim(), ChatId.Trim(), "Telegram Memo test");
            if (messageId is { } id)
                settings.SetLastMessageId(ChatId, id);
            TestMessage = "Test message sent";
            TestState = TestState.Success;
        }
        catch (TelegramException ex)
        {
            ShowTestError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    Task OpenChannelAsync() => channel.OpenAsync(ChatId);

    [RelayCommand]
    async Task SaveAsync()
    {
        ValidationMessage = string.Empty;
        var chatId = ChatId.Trim();
        if (chatId.Length > 0 && !SettingsService.IsValidChatId(chatId))
        {
            ValidationMessage = "Chat ID must be @username or a number starting with -100";
            return;
        }

        await settings.SaveAsync(BotToken, chatId);
        IsConfigured = settings.IsConfigured;

        // Settings may have fixed the cause of failed memos.
        await queue.RequeueAllAsync();
        _ = queue.ProcessAsync();

        await Toasts.ShowAsync(IsConfigured ? "✓  Settings saved" : "Saved. Telegram is not fully set up yet");
    }

    [RelayCommand]
    Task BackAsync() => Shell.Current.GoToAsync("..");

    void ShowTestError(string message)
    {
        TestMessage = message;
        TestState = TestState.Error;
    }
}
