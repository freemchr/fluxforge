using System.Diagnostics;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FluxForge.Application.Jobs;
using FluxForge.Application.Processes;

namespace FluxForge.Desktop.ViewModels;

/// <summary>Live console text, elapsed clock and status shared by the Read and Write pages.</summary>
public sealed partial class JobConsoleViewModel : ObservableObject
{
    private const int MaxChars = 400_000;
    private readonly StringBuilder _log = new();
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer;

    public JobConsoleViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            Elapsed = _clock.Elapsed.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
            FlushLog();
        };
    }

    [ObservableProperty]
    public partial string LogText { get; set; } = "";

    [ObservableProperty]
    public partial string Elapsed { get; set; } = "00:00";

    [ObservableProperty]
    public partial JobStatus Status { get; set; } = JobStatus.Pending;

    [ObservableProperty]
    public partial string? CurrentTrack { get; set; }

    private bool _dirty;

    public void Start(string commandPreview)
    {
        _log.Clear();
        _log.AppendLine("> " + commandPreview);
        LogText = _log.ToString();
        Status = JobStatus.Running;
        CurrentTrack = null;
        _clock.Restart();
        _timer.Start();
    }

    public void Append(OutputLine line)
    {
        _log.AppendLine(line.Text); // gw writes everything to stderr; show it verbatim
        if (_log.Length > MaxChars) _log.Remove(0, _log.Length - MaxChars);
        _dirty = true;
        if (!_timer.IsEnabled) FlushLog(); // late lines delivered after the job ended
    }

    public void Stop(JobStatus status)
    {
        _clock.Stop();
        _timer.Stop();
        Elapsed = _clock.Elapsed.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        Status = status;
        FlushLog();
    }

    public void Note(string text)
    {
        _log.AppendLine(text);
        _dirty = true;
        FlushLog();
    }

    private void FlushLog()
    {
        if (!_dirty) return;
        _dirty = false;
        LogText = _log.ToString();
    }
}
