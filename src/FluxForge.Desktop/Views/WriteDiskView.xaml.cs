using System.Windows.Controls;

namespace FluxForge.Desktop.Views;

public partial class WriteDiskView
{
    public WriteDiskView() => InitializeComponent();

    private void ScrollToEnd(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
