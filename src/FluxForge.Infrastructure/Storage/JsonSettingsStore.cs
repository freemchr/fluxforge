using FluxForge.Application.Settings;

namespace FluxForge.Infrastructure.Storage;

public sealed class JsonSettingsStore(AppPaths paths) : ISettingsStore
{
    private readonly Lock _gate = new();

    public AppSettings Load()
    {
        lock (_gate) return Json.Read<AppSettings>(paths.SettingsFile) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        lock (_gate) Json.WriteAtomic(paths.SettingsFile, settings);
    }
}
