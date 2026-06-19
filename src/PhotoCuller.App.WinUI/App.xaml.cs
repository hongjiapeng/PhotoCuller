using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using PhotoCuller.App.WinUI.Views;
using Serilog;

namespace PhotoCuller.App.WinUI;

public partial class App : Application
{
    private IHost? _host;
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
    }

    public static IServiceProvider Services =>
        ((App)Current)._host!.Services;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host = CreateHost();
        _host.Start();

        _mainWindow = _host.Services.GetRequiredService<MainWindow>();
        _mainWindow.Activate();
    }

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoCuller", "logs", "log-.txt");

    private static IHost CreateHost() =>
        Host.CreateDefaultBuilder()
            .UseSerilog((_, lc) => lc
                .MinimumLevel.Debug()
                .WriteTo.Debug()
                .WriteTo.File(
                    LogPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14))
            .ConfigureServices(services =>
            {
                // UI
                services.AddSingleton<MainWindow>();

                // TODO: 后续阶段注册 Core / Infrastructure 服务
            })
            .Build();
}
