using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Gw;
using FluxForge.Desktop.Services;

namespace FluxForge.Desktop.ViewModels;

public sealed partial class TrackCellViewModel(int cylinder, int head, Action<TrackCellViewModel> select) : ObservableObject
{
    public int Cylinder { get; } = cylinder;
    public int Head { get; } = head;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(Brush), nameof(AccessibleName), nameof(ToolTip))]
    public partial TrackObservation? Observation { get; set; }

    public TrackStatus Status => Observation?.Status ?? TrackStatus.NotRead;
    public string Glyph => StatusVisuals.For(Status).Glyph;
    public System.Windows.Media.Brush Brush => StatusVisuals.For(Status).Brush;
    public string AccessibleName => $"Cylinder {Cylinder}, side {Head}: {StatusVisuals.For(Status).Label}";

    public string ToolTip => Observation is { } o
        ? $"{AccessibleName}" +
          (o.SectorsTotal is { } t ? $"\nSectors: {o.SectorsFound}/{t}" : "") +
          (o.Retries > 0 ? $"\nRetries: {o.Retries}" : "") +
          $"\nParser confidence: {o.Confidence}"
        : AccessibleName;

    [RelayCommand]
    private void Select() => select(this);
}

public sealed partial class TrackSideViewModel(int head, IReadOnlyList<TrackCellViewModel> cells) : ObservableObject
{
    public string Title { get; } = $"Side {head}";
    public IReadOnlyList<TrackCellViewModel> Cells { get; } = cells;
    public IReadOnlyList<string> RowLabels { get; } = [.. Enumerable.Range(0, (cells.Count + 9) / 10).Select(r => (r * 10).ToString(System.Globalization.CultureInfo.InvariantCulture))];
}

/// <summary>
/// Track outcome grid: one panel per side, rows of ten cylinders. Cells only change colour from
/// parsed gw output; anything not observed stays "Not read".
/// </summary>
public sealed partial class TrackMapViewModel : ObservableObject
{
    private readonly Dictionary<(int, int), TrackCellViewModel> _cells = [];

    public ObservableCollection<TrackSideViewModel> Sides { get; } = [];

    [ObservableProperty]
    public partial TrackCellViewModel? Selected { get; set; }

    public static IReadOnlyList<StatusVisual> Legend { get; } =
        [.. Enum.GetValues<TrackStatus>().Select(s => StatusVisuals.For(s))];

    public void Reset(int cylinders, int heads)
    {
        _cells.Clear();
        Sides.Clear();
        Selected = null;
        for (var h = 0; h < heads; h++)
        {
            var cells = new List<TrackCellViewModel>();
            for (var c = 0; c < cylinders; c++)
            {
                var cell = new TrackCellViewModel(c, h, x => Selected = x);
                cells.Add(cell);
                _cells[(c, h)] = cell;
            }
            Sides.Add(new TrackSideViewModel(h, cells));
        }
    }

    public void Update(TrackObservation observation)
    {
        if (!_cells.TryGetValue((observation.Cylinder, observation.Head), out var cell))
        {
            // gw read more cylinders than the profile suggests (e.g. raw capture of 84 tracks): grow the map.
            var existing = _cells.Values.Select(c => c.Observation).OfType<TrackObservation>().ToList();
            Reset(Math.Max(observation.Cylinder + 1, Sides.FirstOrDefault()?.Cells.Count ?? 0), Math.Max(observation.Head + 1, Sides.Count));
            foreach (var o in existing) _cells[(o.Cylinder, o.Head)].Observation = o;
            cell = _cells[(observation.Cylinder, observation.Head)];
        }
        cell.Observation = observation;
    }

    public void Load(IEnumerable<TrackObservation> observations)
    {
        foreach (var o in observations) Update(o);
    }
}
