using Telegram_Memo_App.ViewModels;

namespace Telegram_Memo_App.Views;

public partial class PendingPage : ContentPage
{
    readonly PendingViewModel vm;

    public PendingPage(PendingViewModel vm)
    {
        InitializeComponent();
        BindingContext = this.vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        vm.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        vm.OnDisappearing();
    }
}
