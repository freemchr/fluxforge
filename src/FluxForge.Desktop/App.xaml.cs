using System.Windows;
using System.Windows.Threading;
using FluxForge.Application.Devices;
using FluxForge.Application.Formats;
using FluxForge.Application.Jobs;
using FluxForge.Application.Library;
using FluxForge.Application.Processes;
using FluxForge.Application.Settings;
using FluxForge.Application.Tools;
using FluxForge.Desktop.Services;
using FluxForge.Desktop.ViewModels;
using FluxForge.Infrastructure.Processes;
using FluxForge.Infrastructure.Storage;
using FluxForge.Infrastructure.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluxForge.Desktop;

public partial class App
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var paths = new AppPaths();
        paths.EnsureCreated();

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<JsonJobStore>();
        services.AddSingleton<ILibraryStore>(_ => OpenLibrary(paths));
        services.AddSingleton<LibraryService>();
        services.AddSingleton<IJobStore>(sp => new CataloguingJobStore(sp.GetRequiredService<JsonJobStore>(), sp.GetRequiredService<LibraryService>()));
        services.AddSingleton<JobRunner>();
        services.AddSingleton<IJobActivity>(sp => sp.GetRequiredService<JobRunner>());
        services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"FluxForge/{typeof(App).Assembly.GetName().Version}");
            return new GitHubReleaseClient(http);
        });
        services.AddSingleton<FluxForge.Application.Firmware.IFirmwareReleaseSource>(sp => sp.GetRequiredService<GitHubReleaseClient>());
        services.AddSingleton<IHostToolsManager, HostToolsManager>(sp => new HostToolsManager(
            sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<ISettingsStore>(), paths,
            sp.GetRequiredService<GitHubReleaseClient>(), sp.GetRequiredService<IJobActivity>()));
        services.AddSingleton<DeviceService>();
        services.AddSingleton(new FormatRegistry());

        services.AddSingleton<AppState>();
        services.AddSingleton<AppUpdateService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<INavigator>(sp => sp.GetRequiredService<ShellViewModel>());
        services.AddSingleton<ToolsSetupViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<ReadDiskViewModel>();
        services.AddSingleton<WriteDiskViewModel>();
        services.AddSingleton<VerifyViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<ImageStudioViewModel>();
        services.AddSingleton<DriveToolsViewModel>();
        services.AddSingleton<ActivityViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        _services.GetRequiredService<ThemeService>().ApplySaved();
        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        _ = _services.GetRequiredService<AppState>().InitializeAsync();
        _ = _services.GetRequiredService<AppUpdateService>().CheckAsync(userInitiated: false);
        ImportHistoryIntoNewLibrary(_services);
    }

    /// <summary>A damaged catalogue is set aside (never deleted) and a fresh one started; job files are untouched.</summary>
    private static SqliteLibraryStore OpenLibrary(AppPaths paths)
    {
        try { return new SqliteLibraryStore(paths); }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            var db = Path.Combine(paths.Data, "library.db");
            File.Move(db, $"{db}.damaged-{DateTime.Now:yyyyMMdd-HHmmss}");
            return new SqliteLibraryStore(paths);
        }
    }

    /// <summary>First run with the Library: catalogue everything already in Activity, in the background.</summary>
    private static void ImportHistoryIntoNewLibrary(IServiceProvider services)
    {
        var library = services.GetRequiredService<LibraryService>();
        if (!library.Store.IsNew) return;
        var history = services.GetRequiredService<JsonJobStore>();
        _ = Task.Run(() =>
        {
            try { library.ImportHistory(history.List(int.MaxValue)); }
            catch (Exception e) when (e is IOException or Microsoft.Data.Sqlite.SqliteException)
            {
                System.Diagnostics.Trace.TraceWarning($"Library: history import failed: {e.Message}");
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static string? _lastError;

    /// <summary>Show an unexpected error once. A failing view can re-throw on every render; don't stack dialogs.</summary>
    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (e.Exception.Message == _lastError) return;
        _lastError = e.Exception.Message;
        MessageBox.Show(e.Exception.Message + "\n\nPlease report this via Help & issues.", "FluxForge: unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
