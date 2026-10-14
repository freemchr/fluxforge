using System.Windows.Controls;

namespace FluxForge.Desktop.Views;

public partial class VerifyView
{
    public VerifyView() => InitializeComponent();

    private void ScrollToEnd(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
