using System.IO.Compression;
using FluxForge.Application.Jobs;
using FluxForge.Application.Processes;
using FluxForge.Application.Tools;
using FluxForge.Infrastructure.Storage;
using FluxForge.Infrastructure.Tools;
using FluxForge.Tests.Support;

namespace FluxForge.Tests;

public class HostToolsManagerTests
{
    private sealed class Idle : IJobActivity
    {
        public bool HasActiveJobs { get; set; }
        public bool IsDeviceBusy(string? port) => false;
    }

    /// <summary>Pretend gw.exe: reports the version written inside the file, or fails if the file says "broken".</summary>
    private static FakeProcessRunner VersionFromFile() => new((req, _, _) =>
    {
        var content = File.ReadAllText(req.FileName);
        return Task.FromResult(content == "broken"
            ? FakeProcessRunner.Result(1, "Traceback (most recent call last):")
            : FakeProcessRunner.Result(1, $"Host Tools: {content}", "Device:", "  Port:     Not found"));
    });

    private static string MakeZip(TempDir dir, string name, string gwContent, string folder = "greaseweazle-x/")
    {
        var path = dir.File(name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry(folder + "gw.exe").Open())) w.Write(gwContent);
        using (var w = new StreamWriter(zip.CreateEntry(folder + "COPYING").Open())) w.Write("license");
        return path;
    }

    private static (HostToolsManager Manager, JsonSettingsStore Settings, Idle Jobs) Create(TempDir dir)
    {
        var paths = new AppPaths(dir.File("home"));
        var settings = new JsonSettingsStore(paths);
        var jobs = new Idle();
        var manager = new HostToolsManager(VersionFromFile(), settings, paths, new GitHubReleaseClient(new HttpClient()), jobs, _ => "");
        return (manager, settings, jobs);
    }

    [Fact]
    public async Task Missing_when_nothing_installed()
    {
        using var dir = new TempDir();
        var (manager, _, _) = Create(dir);
        Assert.Equal(HostToolsState.Missing, (await manager.DiscoverAsync(default)).State);
    }

    [Fact]
    public async Task Import_installs_and_activates()
    {
        using var dir = new TempDir();
        var (manager, settings, _) = Create(dir);

        var status = await manager.ImportZipAsync(MakeZip(dir, "gw.zip", "1.23"), null, default);

        Assert.Equal(HostToolsState.Found, status.State);
        Assert.Equal("1.23", settings.Load().ActiveManagedVersion);
        var discovered = await manager.DiscoverAsync(default);
        Assert.Equal(HostToolsSource.Managed, discovered.Active!.Source);
        Assert.Equal("1.23", discovered.Active.Version.ToString());
    }

    [Fact]
    public async Task Failed_install_keeps_last_known_good_active()
    {
        using var dir = new TempDir();
        var (manager, settings, _) = Create(dir);
        await manager.ImportZipAsync(MakeZip(dir, "good.zip", "1.23"), null, default);

        await Assert.ThrowsAsync<ToolsInstallException>(() => manager.ImportZipAsync(MakeZip(dir, "bad.zip", "broken"), null, default));

        Assert.Equal("1.23", settings.Load().ActiveManagedVersion);
        Assert.Equal(HostToolsState.Found, (await manager.DiscoverAsync(default)).State);
        Assert.DoesNotContain(Directory.GetDirectories(dir.File("home/Tools/Greaseweazle")), d => Path.GetFileName(d).StartsWith(".staging"));
    }

    [Fact]
    public async Task Upgrade_then_rollback()
    {
        using var dir = new TempDir();
        var (manager, settings, _) = Create(dir);
        await manager.ImportZipAsync(MakeZip(dir, "old.zip", "1.22"), null, default);
        await manager.ImportZipAsync(MakeZip(dir, "new.zip", "1.23"), null, default);
        Assert.Equal(("1.23", "1.22"), (settings.Load().ActiveManagedVersion, settings.Load().PreviousManagedVersion));

        var status = await manager.RollbackAsync(default);

        Assert.Equal("1.22", status.Active!.Version.ToString());
        Assert.Equal(("1.22", "1.23"), (settings.Load().ActiveManagedVersion, settings.Load().PreviousManagedVersion));
        Assert.Equal(2, manager.ListManaged().Count);
    }

    [Fact]
    public async Task Refuses_to_change_tools_during_a_job()
    {
        using var dir = new TempDir();
        var (manager, _, jobs) = Create(dir);
        jobs.HasActiveJobs = true;
        await Assert.ThrowsAsync<ToolsInstallException>(() => manager.ImportZipAsync(MakeZip(dir, "gw.zip", "1.23"), null, default));
    }

    [Fact]
    public async Task Broken_custom_path_is_reported_not_skipped()
    {
        using var dir = new TempDir();
        var (manager, settings, _) = Create(dir);
        settings.Save(settings.Load() with { CustomGwPath = dir.File("missing\\gw.exe") });
        Assert.Equal(HostToolsState.Failed, (await manager.DiscoverAsync(default)).State);
    }
}
