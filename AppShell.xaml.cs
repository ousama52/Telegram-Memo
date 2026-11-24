using Telegram_Memo_App.Services;
using Telegram_Memo_App.Views;

namespace Telegram_Memo_App
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(Routes.Review, typeof(ReviewPage));
            Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));
            Routing.RegisterRoute(Routes.Pending, typeof(PendingPage));
        }
    }
}
