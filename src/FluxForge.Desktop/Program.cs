using Velopack;

namespace FluxForge.Desktop;

public static class Program
{
    /// <summary>
    /// Velopack must run before anything else: it handles install/uninstall/update hooks and may exit
    /// the process. Then start WPF normally.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
