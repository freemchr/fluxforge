using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Formats;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Library;
using FluxForge.Desktop.Services;

namespace FluxForge.Desktop.ViewModels;

/// <summary>One image of the selected disk, with its file state and what can be done with it.</summary>
public sealed partial class LibraryImageRow(LibraryImage image, FormatRegistry formats) : ObservableObject
{
    public LibraryImage Image { get; } = image;
    public string FileName => Path.GetFileName(Image.Path);
    public string Folder => Path.GetDirectoryName(Image.Path) ?? "";
    public bool IsRaw => Image.Kind == ArtifactKind.RawFlux;
    public bool Exists => File.Exists(Image.Path);

    public string Summary =>
        $"{(IsRaw ? "Raw flux capture" : "Sector image")}{(Image.GwFormat is { } f ? $" · {f}" : "")} · {Image.Bytes / 1024.0:N0} KB";

    public string Provenance => Image.JobKind switch
    {
        JobKind.Read => $"Read from disk {Image.AddedAt.ToLocalTime():d MMM yyyy HH:mm}: {StatusVisuals.For(Image.JobStatus).Label}",
        JobKind.Convert => $"Decoded {Image.AddedAt.ToLocalTime():d MMM yyyy HH:mm}: {StatusVisuals.For(Image.JobStatus).Label}",
        _ => $"Added {Image.AddedAt.ToLocalTime():d MMM yyyy HH:mm}",
    };

    public string? JobDetail => Image.JobDetail;

    public string? VerifyText => Image.LastVerifiedAt is { } at
        ? $"Last verified {at.ToLocalTime():d MMM yyyy HH:mm}: {Image.LastVerifyDetail}"
        : null;

    public System.Windows.Media.Brush? VerifyBrush => Image.LastVerifyStatus is { } s ? StatusVisuals.For(s).Brush : null;

    /// <summary>Result of the last integrity check, or the file's absence (checked whenever the row is built).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntegrityBrush))]
    public partial string? Integrity { get; set; } = File.Exists(image.Path) ? null : "File not found at this path";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntegrityBrush))]
    public partial bool IntegrityOk { get; set; }

    public System.Windows.Media.Brush IntegrityBrush => StatusVisuals.For(IntegrityOk ? JobStatus.Succeeded : JobStatus.Failed).Brush;

    public DiskProfile? Profile => Image.GwFormat is { } f ? formats.Profiles.FirstOrDefault(p => p.GwFormat == f) : null;
}

public sealed record ConditionOption(DiskCondition Value, string Label);

/// <summary>
/// The catalogue: every disk FluxForge has read or decoded (added automatically) plus files added by
/// hand. Only paths and SHA-256 fingerprints are stored; removing an entry never deletes a file.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject, IPageLifecycle
{
    private readonly LibraryService _library;
    private readonly FormatRegistry _formats;
    private readonly IDialogService _dialogs;
    private readonly INavigator _navigator;
    private readonly VerifyViewModel _verify;
    private readonly WriteDiskViewModel _write;
    private readonly ImageStudioViewModel _studio;
    private bool _loading;

    public LibraryViewModel(LibraryService library, FormatRegistry formats, IDialogService dialogs, INavigator navigator,
        VerifyViewModel verify, WriteDiskViewModel write, ImageStudioViewModel studio)
    {
        _library = library;
        _formats = formats;
        _dialogs = dialogs;
        _navigator = navigator;
        _verify = verify;
        _write = write;
        _studio = studio;
        Systems = [.. formats.Profiles.Select(p => p.System).Distinct()];
        // Jobs finish on background threads; refresh on the UI thread.
        library.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => Load());
    }

    public ObservableCollection<LibraryDisk> Disks { get; } = [];
    public ObservableCollection<LibraryImageRow> Images { get; } = [];
    public IReadOnlyList<string> Systems { get; }

    public IReadOnlyList<ConditionOption> Conditions { get; } =
    [
        new(DiskCondition.Unknown, "Not assessed"),
        new(DiskCondition.Good, "Good"),
        new(DiskCondition.Worn, "Worn: some sectors hard to read"),
        new(DiskCondition.Damaged, "Damaged: unreadable areas"),
    ];

    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial LibraryDisk? Selected { get; set; }
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string EditTitle { get; set; } = "";
    [ObservableProperty] public partial string? EditSystem { get; set; }
    [ObservableProperty] public partial ConditionOption? EditCondition { get; set; }
    [ObservableProperty] public partial string EditTags { get; set; } = "";
    [ObservableProperty] public partial string EditNotes { get; set; } = "";
    [ObservableProperty] public partial bool IsDirty { get; set; }

    public bool IsEmpty => Disks.Count == 0 && Search.Length == 0;

    public void OnNavigatedTo() => Load();

    partial void OnSearchChanged(string value) => Load();

    partial void OnSelectedChanged(LibraryDisk? value)
    {
        _loading = true;
        EditTitle = value?.Title ?? "";
        EditSystem = value?.System;
        EditCondition = Conditions.First(c => c.Value == (value?.Condition ?? DiskCondition.Unknown));
        EditTags = value is null ? "" : string.Join(", ", value.Tags);
        EditNotes = value?.Notes ?? "";
        IsDirty = false;
        _loading = false;
        Images.Clear();
        foreach (var image in value?.Images ?? []) Images.Add(new LibraryImageRow(image, _formats));
        RemoveDiskCommand.NotifyCanExecuteChanged();
        CheckFilesCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditTitleChanged(string value) => MarkDirty();
    partial void OnEditSystemChanged(string? value) => MarkDirty();
    partial void OnEditConditionChanged(ConditionOption? value) => MarkDirty();
    partial void OnEditTagsChanged(string value) => MarkDirty();
    partial void OnEditNotesChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (_loading || Selected is null) return;
        IsDirty = true;
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDirtyChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Load()
    {
        // Don't throw away unsaved edits because a job finished in the background.
        if (IsDirty) return;
        var keep = Selected?.Id;
        Disks.Clear();
        foreach (var disk in _library.Store.Search(Search)) Disks.Add(disk);
        Selected = Disks.FirstOrDefault(d => d.Id == keep) ?? Disks.FirstOrDefault();
        var all = Search.Length == 0 ? Disks : _library.Store.Search();
        var images = all.Sum(d => d.Images.Count);
        Summary = Search.Length == 0
            ? $"{Disks.Count:N0} disk{(Disks.Count == 1 ? "" : "s")} · {images:N0} image{(images == 1 ? "" : "s")}"
            : $"{Disks.Count:N0} of {all.Count:N0} disks match";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool CanSave => IsDirty && Selected is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (Selected is not { } disk) return;
        var saved = _library.Update(disk with
        {
            Title = EditTitle,
            System = string.IsNullOrWhiteSpace(EditSystem) ? null : EditSystem.Trim(),
            Condition = EditCondition?.Value ?? DiskCondition.Unknown,
            Tags = LibraryService.ParseTags(EditTags),
            Notes = EditNotes,
        });
        IsDirty = false;
        Load();
        Selected = Disks.FirstOrDefault(d => d.Id == saved.Id);
    }

    [RelayCommand]
    private void Revert() { IsDirty = false; OnSelectedChanged(Selected); }

    private bool HasSelection => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveDiskAsync()
    {
        if (Selected is not { } disk) return;
        if (!await _dialogs.ConfirmAsync("Remove from the Library?",
                $"'{disk.Title}' and its {disk.Images.Count} image record(s) will be removed from the catalogue.\n\nThe image files themselves are not deleted.",
                "Remove"))
            return;
        IsDirty = false;
        _library.Remove(disk.Id);
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        var files = _dialogs.PickFiles("Add disk images to the Library",
            "Disk images (*.adf;*.st;*.img;*.ima;*.d64;*.scp)|*.adf;*.st;*.img;*.ima;*.d64;*.scp|All files (*.*)|*.*");
        if (files.Count == 0) return;
        IsBusy = true;
        try
        {
            var added = await Task.Run(() => _library.AddFilesAsync(files, CancellationToken.None));
            if (added < files.Count)
                await _dialogs.AlertAsync("Already in the Library",
                    $"{files.Count - added} of {files.Count} file(s) were already catalogued (same SHA-256 fingerprint) and were skipped.");
        }
        catch (IOException e) { await _dialogs.AlertAsync("Couldn't add files", e.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CheckFilesAsync()
    {
        IsBusy = true;
        try
        {
            foreach (var row in Images)
            {
                var result = await Task.Run(() => LibraryService.CheckAsync(row.Image, CancellationToken.None));
                row.IntegrityOk = result == IntegrityResult.Unchanged;
                row.Integrity = result switch
                {
                    IntegrityResult.Unchanged => "✓ Fingerprint matches: the file is unchanged since it was catalogued",
                    IntegrityResult.Changed => "✕ The file has changed since it was catalogued (SHA-256 differs)",
                    _ => "✕ File not found at this path",
                };
            }
        }
        catch (IOException e) { await _dialogs.AlertAsync("Couldn't check files", e.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private static void ShowFile(LibraryImageRow row)
    {
        if (row.Exists)
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", row.Image.Path }, UseShellExecute = false });
        else if (Directory.Exists(row.Folder))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { row.Folder }, UseShellExecute = false });
    }

    [RelayCommand]
    private void VerifyDisk(LibraryImageRow row)
    {
        _verify.Prefill(row.Image.Path, row.Profile ?? ProfileOf(Selected));
        _navigator.Navigate(Pages.Verify);
    }

    [RelayCommand]
    private void WriteDisk(LibraryImageRow row)
    {
        _write.ImagePath = row.Image.Path;
        if (!row.IsRaw && (row.Profile ?? ProfileOf(Selected)) is { } profile && _write.ProfileChoices.FirstOrDefault(p => p.Profile?.Id == profile.Id) is { } option)
            _write.SelectedProfile = option;
        _navigator.Navigate(Pages.Write);
    }

    [RelayCommand]
    private void Decode(LibraryImageRow row)
    {
        _studio.InputPath = row.Image.Path;
        _navigator.Navigate(Pages.ImageStudio);
    }

    [RelayCommand]
    private async Task RemoveImageAsync(LibraryImageRow row)
    {
        if (!await _dialogs.ConfirmAsync("Remove this image from the Library?",
                $"{row.FileName} will no longer be listed. The file itself is not deleted.", "Remove"))
            return;
        IsDirty = false;
        _library.RemoveImage(row.Image);
    }

    private DiskProfile? ProfileOf(LibraryDisk? disk) => disk?.ProfileId is { } id ? _formats.Find(id) : null;
}
