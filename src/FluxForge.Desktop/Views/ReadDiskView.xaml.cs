using System.Windows.Controls;

namespace FluxForge.Desktop.Views;

public partial class ReadDiskView
{
    public ReadDiskView() => InitializeComponent();

    private void ScrollToEnd(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
