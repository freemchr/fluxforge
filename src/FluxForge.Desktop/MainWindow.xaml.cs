using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FluxForge.Application.Settings;
using FluxForge.Desktop.Services;
using FluxForge.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace FluxForge.Desktop;

public partial class MainWindow : FluentWindow
{
    // Comfortable size for every page; shrunk to fit smaller screens.
    private const double PreferredWidth = 1320, PreferredHeight = 900, ScreenMargin = 24;

    private readonly ISettingsStore _settings;
    private readonly AppState _state;
    private readonly System.Windows.Threading.DispatcherTimer _usbDebounce;

    public MainWindow(ShellViewModel viewModel, ISettingsStore settings, AppState state)
    {
        _settings = settings;
        _state = state;
        // USB plug/unplug arrives as a burst of WM_DEVICECHANGE messages; re-detect once it settles.
        _usbDebounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _usbDebounce.Tick += async (_, _) =>
        {
            _usbDebounce.Stop();
            if (!_state.IsJobRunning) await _state.DetectDeviceAsync();
        };
        DataContext = viewModel;
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.Manual;
        viewModel.Navigate(Pages.Home);
    }

    /// <summary>
    /// Size and place the window once the monitor and its DPI are known, so the whole window is
    /// always on screen: the last saved placement if it still fits, otherwise a centred window
    /// sized to the monitor's work area (never maximized unless the user left it maximized).
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
        var work = MonitorWorkArea();
        var saved = _settings.Load().Window;

        if (saved is not null && saved.IsVisibleOn(work.Left, work.Top, work.Width, work.Height))
        {
            Width = Math.Clamp(saved.Width, MinWidth, work.Width);
            Height = Math.Clamp(saved.Height, MinHeight, work.Height);
            Left = Math.Clamp(saved.Left, work.Left, work.Right - Width);
            Top = Math.Clamp(saved.Top, work.Top, work.Bottom - Height);
            if (saved.Maximized) WindowState = WindowState.Maximized;
            return;
        }

        Width = Math.Max(MinWidth, Math.Min(PreferredWidth, work.Width - 2 * ScreenMargin));
        Height = Math.Max(MinHeight, Math.Min(PreferredHeight, work.Height - 2 * ScreenMargin));
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
            _settings.Save(_settings.Load() with
            {
                Window = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized),
            });
        base.OnClosing(e);
    }

    /// <summary>Work area (excluding the taskbar) of the monitor this window is on, in WPF units.</summary>
    private Rect MonitorWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(info.Work.Left / dpi.DpiScaleX, info.Work.Top / dpi.DpiScaleY,
            (info.Work.Right - info.Work.Left) / dpi.DpiScaleX, (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
    }

    private const int WmDeviceChange = 0x0219;

    /// <summary>Re-check the Greaseweazle whenever Windows reports USB devices arriving or leaving.</summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange && _state.ToolsReady)
        {
            _usbDebounce.Stop();
            _usbDebounce.Start();
        }
        return IntPtr.Zero;
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
