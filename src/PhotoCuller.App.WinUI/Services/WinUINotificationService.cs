using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoCuller.App.WinUI.Views;
using PhotoCuller.Core.Interfaces.Platform;

namespace PhotoCuller.App.WinUI.Services;

public sealed class WinUINotificationService : INotificationService
{
    private readonly Window _window;

    public WinUINotificationService(MainWindow window) => _window = window;

    public void ShowSuccess(string message) => Show(message, InfoBarSeverity.Success);
    public void ShowWarning(string message) => Show(message, InfoBarSeverity.Warning);
    public void ShowError(string message)   => Show(message, InfoBarSeverity.Error);

    private void Show(string message, InfoBarSeverity severity)
    {
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            if (_window.Content is not FrameworkElement root) return;

            var infoBar = new InfoBar
            {
                Message  = message,
                Severity = severity,
                IsOpen   = true,
                IsClosable = true,
            };

            // Mount inside the MainWindow's notification host if available
            if (root.FindName("NotificationHost") is StackPanel host)
            {
                host.Children.Add(infoBar);
                // Auto-dismiss after 4 s
                var timer = _window.DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(4);
                timer.Tick += (_, _) =>
                {
                    infoBar.IsOpen = false;
                    host.Children.Remove(infoBar);
                    timer.Stop();
                };
                timer.Start();
            }
        });
    }
}
