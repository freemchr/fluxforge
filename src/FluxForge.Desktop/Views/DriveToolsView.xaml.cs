using System.Windows.Controls;

namespace FluxForge.Desktop.Views;

public partial class DriveToolsView
{
    public DriveToolsView() => InitializeComponent();

    private void ScrollToEnd(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
