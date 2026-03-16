using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace DentalID.Desktop.Views;

public partial class SplashWindow : Window
{
    private bool _isClosingWithAnimation;

    public SplashWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opacity = 1;
    }

    public Task CloseWithFadeAsync()
    {
        if (_isClosingWithAnimation)
            return Task.CompletedTask;

        _isClosingWithAnimation = true;
        Opacity = 0;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherTimer.RunOnce(() =>
        {
            Close();
            completion.TrySetResult(true);
        }, TimeSpan.FromMilliseconds(220));

        return completion.Task;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
