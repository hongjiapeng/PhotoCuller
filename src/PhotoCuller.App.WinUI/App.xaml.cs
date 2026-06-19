using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using PhotoCuller.App.WinUI.Services;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.App.WinUI.Views;
using PhotoCuller.Core.Interfaces.Platform;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Configuration;
using Serilog;

namespace PhotoCuller.App.WinUI;

public partial class App : Application
{
    private IHost? _host;
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();

        // Global unhandled exception handlers
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException      += OnUnobservedTask;
    }

    public static IServiceProvider Services =>
        ((App)Current)._host!.Services;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host = CreateHost();
        await _host.StartAsync();

        // Initialise database (applies EF migrations + SQLite PRAGMAs)
        using (var scope = _host.Services.CreateScope())
        {
            var initializer = scope.ServiceProvider
                .GetRequiredService<PhotoCuller.Infrastructure.Database.DatabaseInitializer>();
            await initializer.InitializeAsync();
        }

        _mainWindow = _host.Services.GetRequiredService<MainWindow>();

        // Restore last directory and apply saved theme
        var settings = await _host.Services
            .GetRequiredService<ISettingsRepository>()
            .LoadAsync();

        var mainVm = _host.Services.GetRequiredService<MainViewModel>();
        if (settings.RestoreLastDirectoryOnStartup
            && !string.IsNullOrEmpty(settings.LastSelectedDirectory)
            && Directory.Exists(settings.LastSelectedDirectory))
        {
            mainVm.SelectedDirectory = settings.LastSelectedDirectory;
        }

        // Apply saved theme
        var themeService = _host.Services.GetRequiredService<IThemeService>();
        themeService.ApplyTheme(settings.Theme);

        _mainWindow.Activate();

        Log.Information("PhotoCuller started. Directory: {Dir}",
            mainVm.SelectedDirectory);
    }

    // ── Exception handlers ────────────────────────────────────────────────────

    private static void OnUnhandledException(
        object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled WinUI exception (Handled={Handled})", e.Handled);
        e.Handled = true; // prevent crash; the user sees a notification
    }

    private static void OnDomainException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "AppDomain unhandled exception");
    }

    private static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    // ── Host ─────────────────────────────────────────────────────────────────

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
                    retainedFileCountLimit: 14,
                    outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"))
            .ConfigureServices(services =>
            {
                var dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoCuller", "photoculler.db");
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

                services.AddInfrastructure(dbPath);
                services.AddOptions<AppSettings>();

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

                services.AddSingleton<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<AiModelsViewModel>();
            })
            .Build();
}
