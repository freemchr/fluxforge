using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Jobs;
using FluxForge.Desktop.Services;

namespace FluxForge.Desktop.ViewModels;

public sealed partial class ActivityViewModel(IJobStore store, IDialogService dialogs) : ObservableObject, IPageLifecycle
{
    public ObservableCollection<JobRecord> Jobs { get; } = [];
    public TrackMapViewModel TrackMap { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportText))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand), nameof(ExportReportCommand))]
    public partial JobRecord? Selected { get; set; }

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportText))]
    public partial bool RedactPaths { get; set; }

    public string ReportText => Selected is null ? "" : JobReport.ToText(Selected, RedactPaths);

    public void OnNavigatedTo() => Load();

    partial void OnFilterChanged(string value) => Load();

    partial void OnSelectedChanged(JobRecord? value)
    {
        if (value is null) return;
        var heads = value.Tracks.Count == 0 ? 2 : value.Tracks.Max(t => t.Head) + 1;
        var cyls = value.Tracks.Count == 0 ? 80 : value.Tracks.Max(t => t.Cylinder) + 1;
        TrackMap.IsWrite = value.Kind == JobKind.Write;
        TrackMap.Reset(cyls, heads);
        TrackMap.Load(value.Tracks);
    }

    [RelayCommand]
    private void Load()
    {
        var keep = Selected?.Id;
        Jobs.Clear();
        foreach (var job in store.List().Where(Matches)) Jobs.Add(job);
        Selected = Jobs.FirstOrDefault(j => j.Id == keep) ?? Jobs.FirstOrDefault();
    }

    private bool Matches(JobRecord j) =>
        Filter.Length == 0 ||
        j.Title.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
        (j.DiskLabel?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
        j.Status.ToString().Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
        (j.GwFormat?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool HasSelection => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFolder()
    {
        if (Selected?.LogPath is { } log && Path.GetDirectoryName(log) is { } dir)
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { dir }, UseShellExecute = false });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ExportReportAsync()
    {
        if (Selected is null) return;
        var path = dialogs.SaveFile("Export job report", "Text report (*.txt)|*.txt",
            $"fluxforge-{Selected.StartedAt.ToLocalTime():yyyyMMdd-HHmmss}.txt");
        if (path is null) return;
        await File.WriteAllTextAsync(path, JobReport.ToText(Selected, RedactPaths));
    }
}
