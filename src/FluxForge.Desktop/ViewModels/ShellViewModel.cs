using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace FluxForge.Desktop.ViewModels;

public sealed record NavItem(string Key, string Title, SymbolRegular Icon, string Shortcut);

public sealed partial class ShellViewModel(IServiceProvider services, AppState state) : ObservableObject, INavigator
{
    public AppState State { get; } = state;

    public IReadOnlyList<NavItem> Items { get; } =
    [
        new(Pages.Home, "Home", SymbolRegular.Home24, "Ctrl+1"),
        new(Pages.Read, "Read Disk", SymbolRegular.ArrowDownload24, "Ctrl+2"),
        new(Pages.Write, "Write Disk", SymbolRegular.ArrowUpload24, "Ctrl+3"),
        new(Pages.Verify, "Verify", SymbolRegular.CheckmarkCircle24, "Ctrl+4"),
        new(Pages.ImageStudio, "Image Studio", SymbolRegular.Image24, "Ctrl+5"),
        new(Pages.Library, "Library", SymbolRegular.Library24, "Ctrl+6"),
        new(Pages.DriveTools, "Drive Tools", SymbolRegular.Wrench24, "Ctrl+7"),
        new(Pages.Activity, "Activity", SymbolRegular.History24, "Ctrl+8"),
        new(Pages.Settings, "Settings", SymbolRegular.Settings24, "Ctrl+9"),
    ];

    private readonly Dictionary<string, object> _placeholders = new()
    {
        [Pages.Verify] = new PlaceholderViewModel("Verify", "Phase 2 · v1.0",
            "Backend-supported verification and independent readback comparison. Flux byte comparison is never labelled physical verification."),
        [Pages.ImageStudio] = new PlaceholderViewModel("Image Studio", "Phase 2 · v1.0",
            "Inspect and convert images using only the directional conversions your installed host tools support."),
        [Pages.Library] = new PlaceholderViewModel("Library", "Phase 2 · v1.0",
            "Catalogue disks with hashes, provenance, condition notes, tags and scans. Every job is already recorded under Activity."),
        [Pages.DriveTools] = new PlaceholderViewModel("Drive Tools", "Phase 2 · v1.0",
            "Seek, clean, bandwidth, erase and reset utilities with permission gates, plus the firmware assistant. Firmware is never flashed automatically."),
    };

    [ObservableProperty]
    public partial NavItem? Selected { get; set; }

    [ObservableProperty]
    public partial object? CurrentPage { get; set; }

    partial void OnSelectedChanged(NavItem? value)
    {
        if (value is null) return;
        CurrentPage = value.Key switch
        {
            Pages.Home => services.GetRequiredService<HomeViewModel>(),
            Pages.Read => services.GetRequiredService<ReadDiskViewModel>(),
            Pages.Write => services.GetRequiredService<WriteDiskViewModel>(),
            Pages.Activity => services.GetRequiredService<ActivityViewModel>(),
            Pages.Settings => services.GetRequiredService<SettingsViewModel>(),
            _ => _placeholders[value.Key],
        };
        if (CurrentPage is IPageLifecycle page) page.OnNavigatedTo();
    }

    [RelayCommand]
    public void Navigate(string page) => Selected = Items.First(i => i.Key == page);

    [RelayCommand]
    private void NavigateIndex(string index) => Selected = Items[int.Parse(index, System.Globalization.CultureInfo.InvariantCulture) - 1];
}

public interface IPageLifecycle
{
    void OnNavigatedTo();
}

public sealed record PlaceholderViewModel(string Title, string Phase, string Description);
