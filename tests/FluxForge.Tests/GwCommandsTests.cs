using FluxForge.Application.Formats;
using FluxForge.Application.Gw;

namespace FluxForge.Tests;

public class GwCommandsTests
{
    private static readonly DiskProfile Amiga = FormatRegistry.BuiltIn.Single(p => p.Id == "amiga-dd");
    private static readonly string Dir = Path.GetTempPath();

    [Fact]
    public void Preservation_with_profile_always_adds_raw()
    {
        var args = GwCommands.Read(new ReadRequest(Path.Combine(Dir, "disk.scp"), CaptureMode.Preservation, Amiga, new DeviceTarget()));
        Assert.Equal(["read", "--format=amiga.amigados", "--raw", Path.Combine(Dir, "disk.scp")], args);
    }

    [Fact]
    public void Preservation_without_profile_is_plain_raw_read()
    {
        var args = GwCommands.Read(new ReadRequest(Path.Combine(Dir, "disk.scp"), CaptureMode.Preservation, null, new DeviceTarget()));
        Assert.Equal(["read", Path.Combine(Dir, "disk.scp")], args);
    }

    [Fact]
    public void Preservation_rejects_sector_container()
    {
        Assert.Throws<GwCommandException>(() =>
            GwCommands.Read(new ReadRequest(Path.Combine(Dir, "disk.adf"), CaptureMode.Preservation, Amiga, new DeviceTarget())));
    }

    [Fact]
    public void Sector_image_requires_profile_and_matching_extension()
    {
        Assert.Throws<GwCommandException>(() =>
            GwCommands.Read(new ReadRequest(Path.Combine(Dir, "disk.adf"), CaptureMode.SectorImage, null, new DeviceTarget())));
        Assert.Throws<GwCommandException>(() =>
            GwCommands.Read(new ReadRequest(Path.Combine(Dir, "disk.st"), CaptureMode.SectorImage, Amiga, new DeviceTarget())));
    }

    [Fact]
    public void Sector_read_includes_device_options_in_order()
    {
        var args = GwCommands.Read(new ReadRequest(Path.Combine(Dir, "My Disk.adf"), CaptureMode.SectorImage, Amiga,
            new DeviceTarget("COM7", "b"), Revolutions: 3, Retries: 5));
        Assert.Equal(["read", "--format=amiga.amigados", "--device=COM7", "--drive=B", "--revs=3", "--retries=5", Path.Combine(Dir, "My Disk.adf")], args);
    }

    [Theory]
    [InlineData("disk.adf")]
    [InlineData("..\\disk.adf")]
    [InlineData("-x.adf")]
    public void Relative_paths_are_rejected(string path)
    {
        Assert.Throws<GwCommandException>(() =>
            GwCommands.Read(new ReadRequest(path, CaptureMode.SectorImage, Amiga, new DeviceTarget())));
    }

    [Theory]
    [InlineData("COM3 --erase")]
    [InlineData("--tracks=0")]
    public void Option_values_cannot_inject_arguments(string port)
    {
        Assert.Throws<GwCommandException>(() => GwCommands.Info(new DeviceTarget(port)));
    }

    [Fact]
    public void Unsupported_drive_is_rejected() =>
        Assert.Throws<GwCommandException>(() => GwCommands.Info(new DeviceTarget(null, "C")));

    [Fact]
    public void Write_verifies_by_default()
    {
        var args = GwCommands.Write(new WriteRequest(Path.Combine(Dir, "disk.adf"), Amiga, new DeviceTarget()));
        Assert.DoesNotContain("--no-verify", args);
        Assert.Equal("write", args[0]);
    }

    [Fact]
    public void Preview_quotes_paths_with_spaces() =>
        Assert.Equal("gw.exe read \"C:\\My Disks\\a.scp\"", GwCommands.Preview(@"C:\tools\gw.exe", ["read", @"C:\My Disks\a.scp"]));
}
