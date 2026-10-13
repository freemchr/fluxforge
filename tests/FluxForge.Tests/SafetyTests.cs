using System.IO.Compression;
using FluxForge.Application.Formats;
using FluxForge.Application.Safety;
using FluxForge.Application.Tools;
using FluxForge.Infrastructure.Tools;
using FluxForge.Tests.Support;

namespace FluxForge.Tests;

public class SafetyTests
{
    [Theory]
    [InlineData("../evil.exe")]
    [InlineData("a/../../evil.exe")]
    [InlineData("/abs/evil.exe")]
    [InlineData("C:/evil.exe")]
    public void Zip_traversal_is_rejected(string entryName)
    {
        using var dir = new TempDir();
        var zipPath = dir.File("evil.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            zip.CreateEntry(entryName);
        Assert.Throws<UnsafeArchiveException>(() => SafeZipExtractor.Extract(zipPath, dir.File("out")));
        Assert.False(File.Exists(dir.File("evil.exe")));
    }

    [Fact]
    public void Zip_symlink_is_rejected()
    {
        using var dir = new TempDir();
        var zipPath = dir.File("link.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            zip.CreateEntry("gw.exe").ExternalAttributes = unchecked((int)0xA1FF0000);
        Assert.Throws<UnsafeArchiveException>(() => SafeZipExtractor.Extract(zipPath, dir.File("out")));
    }

    [Fact]
    public void Release_parsing_rejects_prerelease_and_reads_digest()
    {
        const string json = """
            {"tag_name":"v1.23","name":"Greaseweazle Tools v1.23","draft":false,"prerelease":false,
             "html_url":"https://github.com/keirf/greaseweazle/releases/tag/v1.23","published_at":"2025-12-17T10:00:00Z","body":"notes",
             "assets":[
               {"name":"greaseweazle-1.23.zip","browser_download_url":"https://github.com/keirf/greaseweazle/releases/download/v1.23/greaseweazle-1.23.zip","size":10},
               {"name":"greaseweazle-1.23-win64.zip","browser_download_url":"https://github.com/keirf/greaseweazle/releases/download/v1.23/greaseweazle-1.23-win64.zip","size":20,"digest":"sha256:ABCD"}]}
            """;
        var release = GitHubReleaseClient.ParseRelease(json);
        Assert.Equal("1.23", release.Version.ToString());
        Assert.Equal("greaseweazle-1.23-win64.zip", release.WindowsAsset!.Name);
        Assert.Equal("abcd", release.WindowsAsset.Sha256Digest);

        Assert.Throws<ToolsInstallException>(() => GitHubReleaseClient.ParseRelease(json.Replace("\"prerelease\":false", "\"prerelease\":true")));
    }

    [Fact]
    public void Asset_on_untrusted_host_is_ignored()
    {
        var asset = new ReleaseAsset("gw-win64.zip", new Uri("https://evil.example.com/gw-win64.zip"), 1, null);
        Assert.Null(GitHubReleaseClient.SelectWindowsAsset([asset]));
    }

    [Fact]
    public void Write_check_requires_format_for_ambiguous_images()
    {
        using var dir = new TempDir();
        var img = dir.File("disk.img");
        File.WriteAllBytes(img, new byte[737280]);
        Assert.Contains(OutputGuard.CheckWriteImage(img, null), i => i.Blocking);
        var pc720 = FormatRegistry.BuiltIn.Single(p => p.Id == "ibm-720");
        Assert.DoesNotContain(OutputGuard.CheckWriteImage(img, pc720), i => i.Blocking);
    }

    [Fact]
    public void Incomplete_names_do_not_collide()
    {
        using var dir = new TempDir();
        var final = dir.File("disk.adf");
        File.WriteAllText(OutputGuard.IncompleteName(final), "");
        Assert.EndsWith("disk (INCOMPLETE 2).adf", OutputGuard.IncompleteName(final));
    }
}
