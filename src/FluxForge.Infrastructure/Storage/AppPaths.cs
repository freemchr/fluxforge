namespace FluxForge.Infrastructure.Storage;

/// <summary>Per-user data layout (PRD §8). Override the root with FLUXFORGE_HOME for portable mode or tests.</summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root
               ?? Environment.GetEnvironmentVariable("FLUXFORGE_HOME")
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxForge");
    }

    public string Root { get; }
    public string ToolsRoot => Path.Combine(Root, "Tools", "Greaseweazle");
    public string Downloads => Path.Combine(Root, "Cache", "downloads");
    public string Logs => Path.Combine(Root, "Logs");
    public string Jobs => Path.Combine(Root, "Jobs");
    public string Config => Path.Combine(Root, "Config");
    public string Data => Path.Combine(Root, "Data");
    public string SettingsFile => Path.Combine(Config, "settings.json");

    public void EnsureCreated()
    {
        foreach (var dir in new[] { ToolsRoot, Downloads, Logs, Jobs, Config, Data })
            Directory.CreateDirectory(dir);
    }
}
