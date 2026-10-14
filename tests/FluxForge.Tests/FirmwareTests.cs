using FluxForge.Application.Firmware;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Processes;
using FluxForge.Application.Tools;
using FluxForge.Infrastructure.Storage;
using FluxForge.Infrastructure.Tools;
using FluxForge.Tests.Support;

namespace FluxForge.Tests;

public class FirmwareTests
{
    private static readonly HostToolsInstallation Tools = new(@"C:\gw\gw.exe", new GwVersion(1, 23), HostToolsSource.Managed, false);

    private static string Fixture(params string[] parts) => File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "fixtures", .. parts]));

    private static string[] Lines(params string[] parts) => Fixture(parts).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static ReleaseInfo Release(string tag) => new(tag, GwVersion.TryParse(tag, out var v) ? v : default, tag,
        new Uri("https://github.com/keirf/greaseweazle-firmware/releases/tag/" + tag), DateTimeOffset.UnixEpoch, "", null);

    private static readonly GwInfo V41 = new(new GwVersion(1, 23), true, "COM6", "Greaseweazle V4.1", "1.6", null, null, false);

    [Fact]
    public void Real_skip_output_means_nothing_was_written()
    {
        var result = FirmwareAssistant.ParseUpdate(Lines("gw-1.23", "firmware", "update-skip", "stderr.txt"));

        Assert.Equal(FirmwareUpdateOutcome.AlreadyCurrent, result.Outcome);
        Assert.Equal(new GwVersion(1, 6), result.TargetVersion);
        Assert.Equal("Device is already running version 1.6.", result.Message);
    }

    [Theory] // result lines from upstream tools/update.py (gw 1.23)
    [InlineData(FirmwareUpdateOutcome.Flashed, "Downloading latest firmware: greaseweazle-firmware-1.7.upd", "Updating Main Firmware to version 1.7...", "Done.")]
    [InlineData(FirmwareUpdateOutcome.Failed, "Updating Main Firmware to version 1.7...", "** UPDATE FAILED: Please retry!")]
    [InlineData(FirmwareUpdateOutcome.Failed, "Command Failed: Bad Command")]
    [InlineData(FirmwareUpdateOutcome.Failed, "ERROR: New firmware is too large for this device (insufficient Flash memory)")]
    [InlineData(FirmwareUpdateOutcome.Unknown, "Updating Main Firmware to version 1.7...")]
    public void Update_outcome_comes_from_gw_result_lines(FirmwareUpdateOutcome expected, params string[] lines) =>
        Assert.Equal(expected, FirmwareAssistant.ParseUpdate(lines).Outcome);

    [Fact]
    public void Update_jumper_instruction_is_noticed() =>
        Assert.True(FirmwareAssistant.ParseUpdate(["Done.", "** Unplug device and remove the Update Jumper"]).NeedsJumperRemoved);

    [Fact]
    public void Update_command_is_pinned_and_never_touches_the_bootloader()
    {
        var args = GwCommands.UpdateFirmware(new GwVersion(1, 7), new DeviceTarget("COM6", "B"));

        Assert.Equal(["update", "--tag=v1.7", "--device=COM6"], args);
        Assert.DoesNotContain(args, a => a.Contains("bootloader") || a.Contains("force"));
        var help = Fixture("gw-1.23", "tools", "update-help", "stderr.txt");
        Assert.Matches(@"\s--tag\b", help);
        Assert.Matches(@"\s--device\b", help);
    }

    [Fact]
    public void Latest_firmware_release_parses_from_real_github_json()
    {
        var release = GitHubReleaseClient.ParseRelease(Fixture("github", "firmware-latest.json"));

        Assert.Equal(new GwVersion(1, 6), release.Version);
        Assert.Equal("v1.6", release.Tag);
        Assert.Equal(FirmwareAdvice.UpToDate, FirmwareAssistant.Advise("1.6", release));
        Assert.Equal(FirmwareAdvice.UpdateAvailable, FirmwareAssistant.Advise("1.5", release));
        Assert.Equal(FirmwareAdvice.NewerThanRelease, FirmwareAssistant.Advise("1.7", release));
        Assert.Equal(FirmwareAdvice.Unknown, FirmwareAssistant.Advise(null, release));
    }

    private static FakeProcessRunner Gw(string[] updateOutput, string firmwareAfter) => new((req, output, _) => Task.FromResult(req.Arguments[0] switch
    {
        "update" => FakeProcessRunner.Emit(output, 0, updateOutput),
        "info" => FakeProcessRunner.Emit(output, 0, "Host Tools: 1.23", "Device:", "  Port:     COM6", "  Model:    Greaseweazle V4.1", $"  Firmware: {firmwareAfter}"),
        _ => FakeProcessRunner.Result(1),
    }));

    private static JobRunner Jobs(TempDir dir, FakeProcessRunner runner) =>
        new(runner, new JsonJobStore(new AppPaths(dir.File("home")))) { FirmwareRestartDelay = TimeSpan.Zero };

    [Fact]
    public async Task Flash_is_only_success_when_the_board_reports_the_new_version()
    {
        using var dir = new TempDir();
        var runner = Gw(["Updating Main Firmware to version 1.7...", "Done."], "1.7");

        var record = await Jobs(dir, runner).RunFirmwareUpdateAsync(Tools, Release("v1.7"), new DeviceTarget("COM6"), V41, null);

        Assert.Equal(JobStatus.Succeeded, record.Status);
        Assert.Equal("Firmware updated to 1.7, confirmed by the board.", record.StatusDetail);
        Assert.Equal("1.7", record.Firmware);
        Assert.Equal(JobKind.FirmwareUpdate, record.Kind);
        Assert.Equal(["update", "info"], runner.Requests.Select(r => r.Arguments[0]));
    }

    [Fact]
    public async Task Done_without_confirmation_is_not_success()
    {
        using var dir = new TempDir();
        var record = await Jobs(dir, Gw(["Updating Main Firmware to version 1.7...", "Done."], "1.6"))
            .RunFirmwareUpdateAsync(Tools, Release("v1.7"), new DeviceTarget(), V41, null);

        Assert.Equal(JobStatus.CompletedUnverified, record.Status);
    }

    [Fact]
    public async Task Real_skip_path_succeeds_without_writing()
    {
        using var dir = new TempDir();
        var record = await Jobs(dir, Gw(Lines("gw-1.23", "firmware", "update-skip", "stderr.txt"), "1.6"))
            .RunFirmwareUpdateAsync(Tools, Release("v1.6"), new DeviceTarget(), V41, null);

        Assert.Equal(JobStatus.Succeeded, record.Status);
        Assert.StartsWith("Nothing was written: Device is already running version 1.6.", record.StatusDetail);
    }

    [Fact]
    public async Task Failed_flash_explains_recovery_and_skips_the_version_check()
    {
        using var dir = new TempDir();
        var runner = Gw(["Updating Main Firmware to version 1.7...", "** UPDATE FAILED: Please retry!"], "1.6");

        var record = await Jobs(dir, runner).RunFirmwareUpdateAsync(Tools, Release("v1.7"), new DeviceTarget(), V41, null);

        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.Contains("recovery", record.StatusDetail);
        Assert.Equal(["update"], runner.Requests.Select(r => r.Arguments[0]));
    }
}
