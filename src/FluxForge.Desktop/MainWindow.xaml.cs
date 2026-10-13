using System.Windows;
using FluxForge.Desktop.Services;
using FluxForge.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace FluxForge.Desktop;

public partial class MainWindow : FluentWindow
{
    public MainWindow(ShellViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        FitToWorkArea();
        viewModel.Navigate(Pages.Home);
    }

    /// <summary>Open at the compact default size, but never larger than 85% of the screen's work area.</summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width * 0.85));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height * 0.85));
    }
}
