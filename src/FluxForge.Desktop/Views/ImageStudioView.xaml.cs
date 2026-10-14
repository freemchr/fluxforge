using System.Windows.Controls;

namespace FluxForge.Desktop.Views;

public partial class ImageStudioView
{
    public ImageStudioView() => InitializeComponent();

    private void ScrollToEnd(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
