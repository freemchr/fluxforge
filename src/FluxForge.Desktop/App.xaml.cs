using System.Windows;
using System.Windows.Threading;
using FluxForge.Application.Devices;
using FluxForge.Application.Formats;
using FluxForge.Application.Jobs;
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
        services.AddSingleton<IJobStore, JsonJobStore>();
        services.AddSingleton<JobRunner>();
        services.AddSingleton<IJobActivity>(sp => sp.GetRequiredService<JobRunner>());
        services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"FluxForge/{typeof(App).Assembly.GetName().Version}");
            return new GitHubReleaseClient(http);
        });
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
        services.AddSingleton<ImageStudioViewModel>();
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "FluxForge — unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
