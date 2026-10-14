using System.Text.RegularExpressions;
using Wpf.Ui.Controls;

namespace FluxForge.Tests;

/// <summary>
/// XAML icon names are only resolved at runtime, so a typo crashes a page. Check every icon the app
/// uses against the real WPF-UI SymbolRegular enum.
/// </summary>
public partial class IconNameTests
{
    [Fact]
    public void Every_icon_name_used_in_the_app_exists()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "desktop-src");
        var used = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => IconReference().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Name: m.Groups["name"].Value)))
            .ToList();

        Assert.NotEmpty(used);
        var missing = used.Where(u => !Enum.IsDefined(typeof(SymbolRegular), u.Name)).Select(u => $"{u.File}: {u.Name}").Distinct().ToList();
        Assert.True(missing.Count == 0, "Unknown icons: " + string.Join(", ", missing));
    }

    [GeneratedRegex(@"(?:SymbolIcon |Symbol=""|SymbolRegular\.)(?<name>[A-Z][A-Za-z0-9]+\d\d)")]
    private static partial Regex IconReference();
}
