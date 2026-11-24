namespace Telegram_Memo_App.Models;

// Sent rows are deleted, so there is no Sent value.
public enum MemoStatus
{
    Pending = 0,
    Sending = 1,
    Failed = 2,
}
