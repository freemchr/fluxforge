using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Jobs;
using FluxForge.Desktop.Services;

namespace FluxForge.Desktop.ViewModels;

public sealed record ChecklistItem(string Text, bool Done)
{
    public string Glyph => Done ? "✓" : "○";
}

public sealed partial class HomeViewModel : ObservableObject, IPageLifecycle
{
    private readonly IJobStore _jobs;
    private readonly INavigator _navigator;

    public HomeViewModel(AppState state, ToolsSetupViewModel setup, IJobStore jobs, INavigator navigator)
    {
        State = state;
        Setup = setup;
        _jobs = jobs;
        _navigator = navigator;
        state.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Checklist));
    }

    public AppState State { get; }
    public ToolsSetupViewModel Setup { get; }
    public ObservableCollection<JobRecord> RecentJobs { get; } = [];

    public IReadOnlyList<ChecklistItem> Checklist =>
    [
        new("Install Greaseweazle host tools", State.ToolsReady),
        new("Connect your Greaseweazle by USB", State.DeviceReady),
        new("Back up your first disk", RecentJobs.Any(j => j.Kind == JobKind.Read && j.Status is JobStatus.Succeeded or JobStatus.Partial or JobStatus.CompletedUnverified)),
    ];

    public void OnNavigatedTo()
    {
        RecentJobs.Clear();
        foreach (var job in _jobs.List(6)) RecentJobs.Add(job);
        OnPropertyChanged(nameof(Checklist));
    }

    [RelayCommand]
    private Task DetectDeviceAsync() => State.DetectDeviceAsync();

    [RelayCommand]
    private void Go(string page) => _navigator.Navigate(page);

    [RelayCommand]
    private async Task ReviewUpdateAsync()
    {
        if (State.LatestRelease is { } release) await Setup.ReviewAndInstallAsync(release);
    }

    [RelayCommand]
    private void DismissUpdate() => State.UpdateDismissed = true;

    [RelayCommand]
    private void SkipUpdate() => State.SkipLatestRelease();
}
