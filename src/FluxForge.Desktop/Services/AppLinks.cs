using System.Diagnostics;

namespace FluxForge.Desktop.Services;

/// <summary>Project links shown in the app. Only https URLs from this list are ever opened.</summary>
public static class AppLinks
{
    public const string Repository = "https://github.com/freemchr/fluxforge";
    public const string Issues = Repository + "/issues";
    public const string NewIssue = Repository + "/issues/new";
    public const string Greaseweazle = "https://github.com/keirf/greaseweazle";
    public const string GreaseweazleWiki = "https://github.com/keirf/greaseweazle/wiki";

    public static void Open(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
