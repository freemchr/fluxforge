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
    [NotifyPropertyChangedFor(nameof(Status), nameof(Glyph), nameof(Brush), nameof(AccessibleName), nameof(ToolTip))]
    public partial TrackObservation? Observation { get; set; }

    /// <summary>Shown while the drive is on this track (before gw reports it); drives the pulse animation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(Glyph), nameof(Brush), nameof(AccessibleName), nameof(ToolTip))]
    public partial TrackStatus? InProgress { get; set; }

    public bool IsCurrent => InProgress is not null;

    partial void OnInProgressChanged(TrackStatus? value) => OnPropertyChanged(nameof(IsCurrent));

    public TrackStatus Status => InProgress ?? Observation?.Status ?? TrackStatus.NotRead;
    public string Glyph => StatusVisuals.For(Status).Glyph;
    public System.Windows.Media.Brush Brush => StatusVisuals.For(Status).Brush;
    public string AccessibleName => $"Cylinder {Cylinder}, side {Head}: {StatusVisuals.For(Status).Label}";

    public string ToolTip => Observation is { } o
        ? $"{AccessibleName}" +
          (o.SectorsTotal is { } t ? $"\nSectors: {o.SectorsFound}/{t}" : "") +
          (o.Retries > 0 ? $"\nRetries: {o.Retries}" : "") +
          (o.UnexpectedSectors > 0 ? $"\nSectors outside the selected format: {o.UnexpectedSectors}" : "") +
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

    private static readonly TrackStatus[] ReadLegend =
        [TrackStatus.NotRead, TrackStatus.Reading, TrackStatus.DecodedOk, TrackStatus.RecoveredAfterRetry, TrackStatus.Unreadable, TrackStatus.FluxCapturedDecodeUnknown];
    private static readonly TrackStatus[] WriteLegend =
        [TrackStatus.NotRead, TrackStatus.Writing, TrackStatus.Written, TrackStatus.Verified];

    private TrackCellViewModel? _current;

    /// <summary>Write maps show the write legend; everything else the read legend.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Legend))]
    public partial bool IsWrite { get; set; }

    public IReadOnlyList<StatusVisual> Legend =>
        [.. (IsWrite ? WriteLegend : ReadLegend).Select(s => StatusVisuals.For(s))];

    /// <summary>
    /// Live progress: record the finished track, then pulse the track gw will do next. gw works
    /// cylinder by cylinder, side 0 then side 1, so the next track is predictable.
    /// </summary>
    public void Advance(TrackObservation done)
    {
        Update(done);
        SetCurrent(null);
        var heads = Sides.Count;
        var (c, h) = done.Head + 1 < heads ? (done.Cylinder, done.Head + 1) : (done.Cylinder + 1, 0);
        if (_cells.TryGetValue((c, h), out var next) && next.Observation is null)
            SetCurrent(next);
    }

    /// <summary>Pulse the first track while gw seeks and spins up.</summary>
    public void Start() => SetCurrent(_cells.TryGetValue((0, 0), out var first) ? first : null);

    public void Stop() => SetCurrent(null);

    private void SetCurrent(TrackCellViewModel? cell)
    {
        if (_current is not null) _current.InProgress = null;
        _current = cell;
        if (cell is not null) cell.InProgress = IsWrite ? TrackStatus.Writing : TrackStatus.Reading;
    }

    public void Reset(int cylinders, int heads)
    {
        _current = null;
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
