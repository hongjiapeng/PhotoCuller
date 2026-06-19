using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using PhotoCuller.App.WinUI.Services;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.App.WinUI.Views;
using PhotoCuller.Core.Interfaces.Platform;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Configuration;
using Serilog;

namespace PhotoCuller.App.WinUI;

public partial class App : Application
{
    private IHost? _host;
    private MainWindow? _mainWindow;

    public App() => InitializeComponent();

    public static IServiceProvider Services =>
        ((App)Current)._host!.Services;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host = CreateHost();
        await _host.StartAsync();

        // Initialize database on startup
        using (var scope = _host.Services.CreateScope())
        {
            var initializer = scope.ServiceProvider
                .GetRequiredService<PhotoCuller.Infrastructure.Database.DatabaseInitializer>();
            await initializer.InitializeAsync();
        }

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
                var dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoCuller", "photoculler.db");
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

                // Infrastructure
                services.AddInfrastructure(dbPath);

                // AppSettings
                services.AddOptions<AppSettings>();

                // Platform services (need MainWindow, registered after)
                services.AddSingleton<MainWindow>(sp =>
                {
                    var vm = sp.GetRequiredService<MainViewModel>();
                    return new MainWindow(vm);
                });
                services.AddSingleton<IFileDialogService>(sp =>
                    new WinUIFileDialogService(sp.GetRequiredService<MainWindow>()));
                services.AddSingleton<INotificationService>(sp =>
                    new WinUINotificationService(sp.GetRequiredService<MainWindow>()));
                services.AddSingleton<IThemeService>(sp =>
                    new WinUIThemeService(sp.GetRequiredService<MainWindow>()));
                services.AddSingleton<IOpenFileLocationService,
                    WinUIOpenFileLocationService>();

                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<AiModelsViewModel>();
            })
            .Build();
}
