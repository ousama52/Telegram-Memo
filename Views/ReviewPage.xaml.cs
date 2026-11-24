using Telegram_Memo_App.ViewModels;

namespace Telegram_Memo_App.Views;

public partial class ReviewPage : ContentPage
{
    readonly ReviewViewModel vm;

    public ReviewPage(ReviewViewModel vm)
    {
        InitializeComponent();
        BindingContext = this.vm = vm;
        vm.Load();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        // Large photo, but leave room for the info row and note on small screens.
        if (width > 0 && height > 0)
            PhotoFrame.HeightRequest = Math.Min((width - 32) * 1.25, height * 0.58);
    }

    protected override bool OnBackButtonPressed()
    {
        if (PreviewOverlay.IsVisible)
        {
            _ = HidePreviewAsync();
            return true;
        }
        return base.OnBackButtonPressed();
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        // Leaving by hardware back counts as Retake.
        vm.OnLeftWithoutSending();
    }

    async void OnNoteFocused(object? sender, FocusEventArgs e)
    {
        // Wait for the keyboard to resize the page, then keep the note in view.
        await Task.Delay(300);
        await Scroller.ScrollToAsync(NoteEditor, ScrollToPosition.End, true);
    }

    async void OnPreviewClicked(object? sender, EventArgs e)
    {
        NoteEditor.Unfocus();
        vm.BuildPreview();
        PreviewOverlay.IsVisible = true;
        PreviewSheet.TranslationY = Height;
        await Task.WhenAll(
            PreviewDim.FadeToAsync(1, 200),
            PreviewSheet.TranslateToAsync(0, 0, 250, Easing.CubicOut));
    }

    async void OnPreviewClose(object? sender, EventArgs e) => await HidePreviewAsync();

    async Task HidePreviewAsync()
    {
        await Task.WhenAll(
            PreviewDim.FadeToAsync(0, 150),
            PreviewSheet.TranslateToAsync(0, Height, 200, Easing.CubicIn));
        PreviewOverlay.IsVisible = false;
    }
}
