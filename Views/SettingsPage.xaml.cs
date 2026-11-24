using Telegram_Memo_App.ViewModels;

namespace Telegram_Memo_App.Views;

public partial class SettingsPage : ContentPage
{
    readonly SettingsViewModel vm;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = this.vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vm.LoadAsync();
    }
}
