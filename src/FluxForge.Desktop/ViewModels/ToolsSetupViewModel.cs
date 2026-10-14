using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Settings;
using FluxForge.Application.Tools;
using FluxForge.Desktop.Services;
using FluxForge.Infrastructure.Storage;
using FluxForge.Infrastructure.Tools;

namespace FluxForge.Desktop.ViewModels;

/// <summary>
/// User-approved install/update of the official host tools. Nothing is downloaded until the user has
/// seen the publisher, version, source, size and target folder and pressed Install.
/// </summary>
public sealed partial class ToolsSetupViewModel(AppState state, IHostToolsManager tools, ISettingsStore settings, AppPaths paths, IDialogService dialogs)
    : ObservableObject
{
    public AppState State { get; } = state;
    public string TargetFolder { get; } = paths.ToolsRoot;
    public string Publisher => $"Keir Fraser · github.com/{GitHubReleaseClient.Repository} (official)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OfferSize))]
    public partial ReleaseInfo? Offer { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepareInstallCommand), nameof(InstallCommand), nameof(ImportZipCommand), nameof(UseOwnCommand), nameof(RollbackCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool ProgressIndeterminate { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string OfferSize => Offer?.WindowsAsset is { } a ? $"{a.Name} · {a.Size / 1024.0 / 1024.0:0.0} MB" : "No Windows build found in this release";

    private bool CanAct => !IsBusy && !State.IsJobRunning;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task PrepareInstallAsync()
    {
        await RunAsync("Contacting GitHub…", async () =>
        {
            Offer = await tools.GetLatestReleaseAsync(CancellationToken.None);
            State.LatestRelease = Offer;
        });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task InstallAsync()
    {
        if (Offer is not { } release) return;
        await RunAsync("Starting download…", async () =>
        {
            State.ToolsStatus = await tools.InstallAsync(release, Reporter(), CancellationToken.None);
            Offer = null;
            await State.DetectDeviceAsync();
        });
    }

    [RelayCommand]
    private void DismissOffer() => Offer = null;

    /// <summary>
    /// Install a specific release (newer or older) after the user reviews where it comes from.
    /// Returns true when it was installed and activated.
    /// </summary>
    public async Task<bool> ReviewAndInstallAsync(ReleaseInfo release)
    {
        if (!CanAct) return false;
        var current = State.Tools?.Version;
        var direction = current is not { } v ? ""
            : release.Version < v ? $"\n\nThis is OLDER than the version you're using ({v}). Use this if you depend on that release's behaviour."
            : release.Version == v ? $"\n\nThis is the version you're already using."
            : $"\n\nThis updates you from {v}.";
        var message =
            $"Publisher:  {Publisher}\n" +
            $"Release:    {release.Name} (published {release.PublishedAt:d MMM yyyy})\n" +
            $"Source:     {release.HtmlUrl}\n" +
            $"File:       {(release.WindowsAsset is { } a ? $"{a.Name}, {a.Size / 1024.0 / 1024.0:0.0} MB" : "no Windows build in this release")}\n" +
            $"Install to: {TargetFolder}" +
            direction +
            "\n\nIt becomes the active version. Your other installed versions are kept, and you can switch between them in Settings at any time.";
        if (!await dialogs.ConfirmAsync($"Install Greaseweazle host tools {release.Version}?", message, "Download and install"))
            return false;

        var ok = false;
        await RunAsync("Starting download…", async () =>
        {
            State.ToolsStatus = await tools.InstallAsync(release, Reporter(), CancellationToken.None);
            Offer = null;
            ok = true;
            await State.DetectDeviceAsync();
        });
        return ok;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ImportZipAsync()
    {
        var zip = dialogs.PickFile("Select a downloaded Greaseweazle host tools ZIP", "ZIP archives (*.zip)|*.zip");
        if (zip is null) return;
        await RunAsync("Importing…", async () =>
        {
            State.ToolsStatus = await tools.ImportZipAsync(zip, Reporter(), CancellationToken.None);
            await State.DetectDeviceAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task UseOwnAsync()
    {
        var exe = dialogs.PickFile("Select your existing gw.exe", "Greaseweazle host tools (gw.exe)|gw.exe");
        if (exe is null) return;
        await RunAsync("Checking gw.exe…", async () =>
        {
            settings.Save(settings.Load() with { CustomGwPath = exe });
            await State.RefreshToolsAsync();
            await State.DetectDeviceAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task RollbackAsync()
    {
        if (!await dialogs.ConfirmAsync("Roll back host tools", "Switch back to the previous managed Greaseweazle host tools version?", "Roll back")) return;
        await RunAsync("Rolling back…", async () => State.ToolsStatus = await tools.RollbackAsync(CancellationToken.None));
    }

    private Progress<InstallProgress> Reporter() => new(p =>
    {
        ProgressText = p.Stage;
        ProgressIndeterminate = p.Fraction is null;
        ProgressValue = (p.Fraction ?? 0) * 100;
    });

    private async Task RunAsync(string start, Func<Task> work)
    {
        IsBusy = true;
        Error = null;
        ProgressText = start;
        ProgressIndeterminate = true;
        try { await work(); ProgressText = null; }
        catch (Exception e) when (e is ToolsInstallException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Error = e.Message;
            ProgressText = null;
        }
        finally { IsBusy = false; }
    }
}
