// FluxForge FakeGw — a development stand-in for Greaseweazle's gw.exe.
// It imitates the 1.2x console output closely enough to drive the UI without hardware, and every run
// prints the FLUXFORGE-SIMULATED marker so FluxForge labels results as simulated. Never ship this.
//
// Scenarios (env FLUXFORGE_FAKEGW_SCENARIO): ok (default) | nodevice | badtracks | fail
// Speed (env FLUXFORGE_FAKEGW_DELAY_MS): per-track delay, default 25.
// Firmware (env FLUXFORGE_FAKEGW_FIRMWARE): version the board reports until a simulated update, default 1.6.

using System.Globalization;

const string Marker = "FLUXFORGE-SIMULATED";
const string HostVersion = "1.23";
var scenario = (Environment.GetEnvironmentVariable("FLUXFORGE_FAKEGW_SCENARIO") ?? "ok").ToLowerInvariant();
var delay = int.TryParse(Environment.GetEnvironmentVariable("FLUXFORGE_FAKEGW_DELAY_MS"), out var d) ? d : 25;

if (args.Length == 0)
{
    Console.WriteLine("Usage: gw [--time] action [-h] ...");
    return 1;
}

var action = args[0];
var options = args.Skip(1).Where(a => a.StartsWith("--", StringComparison.Ordinal))
    .Select(a => a.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "", StringComparer.Ordinal);
var positional = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();

switch (action)
{
    case "info": return Info();
    case "read": return Read();
    case "write": return Write();
    case "reset": Console.WriteLine($"{Marker} reset"); return 0;
    case "update": return Update();
    default:
        Console.Error.WriteLine($"** FATAL ERROR: Unknown action '{action}'");
        return 1;
}

int Info()
{
    Console.WriteLine($"Host Tools: {HostVersion}   [{Marker}]");
    Console.WriteLine("Device:");
    if (scenario == "nodevice")
    {
        Console.WriteLine("  Port:     Not found");
        Console.Error.WriteLine("** FATAL ERROR: Cannot find the Greaseweazle device");
        return 1;
    }
    Console.WriteLine("  Port:     COM7");
    Console.WriteLine("  Model:    Greaseweazle V4.1 (simulated)");
    Console.WriteLine("  MCU:      AT32F403A, 240MHz, 224kB SRAM");
    Console.WriteLine($"  Firmware: {Firmware()}");
    Console.WriteLine("  Serial:   FAKE0000000001");
    Console.WriteLine("  USB:      Full Speed (12 Mbit/s), 128kB Buffer");
    return 0;
}

// The simulated board remembers a flashed version in a temp file, as a real board keeps it in flash.
string FirmwareStateFile() => Path.Combine(Path.GetTempPath(), "fluxforge-fakegw-firmware.txt");

string Firmware() => File.Exists(FirmwareStateFile()) ? File.ReadAllText(FirmwareStateFile()).Trim()
    : Environment.GetEnvironmentVariable("FLUXFORGE_FAKEGW_FIRMWARE") ?? "1.6";

// Mirrors gw 1.23 tools/update.py output: download, "Updating Main Firmware to version X.Y...", then "Done." or a skip.
int Update()
{
    var version = options.TryGetValue("--tag", out var tag) ? tag.TrimStart('v') : "1.6";
    Console.Error.WriteLine($"Downloading latest firmware: greaseweazle-firmware-{version}.upd   [{Marker}]");
    Console.Error.WriteLine($"Updating Main Firmware to version {version}...");
    if (string.CompareOrdinal(Firmware(), version) >= 0)
    {
        Console.Error.WriteLine("** SKIPPING UPDATE:");
        Console.Error.WriteLine($"Device is already running version {Firmware()}.");
        Console.Error.WriteLine("Use --force to update anyway.");
        return 0;
    }
    Thread.Sleep(delay * 40);
    if (scenario == "fail") { Console.Error.WriteLine("** UPDATE FAILED: Please retry!"); return 0; }
    File.WriteAllText(FirmwareStateFile(), version);
    Console.Error.WriteLine("Done.");
    return 0;
}

int Read()
{
    if (scenario == "nodevice") return Info();
    if (positional.Count != 1) { Console.Error.WriteLine("** FATAL ERROR: expected one output file"); return 1; }
    var output = positional[0];
    options.TryGetValue("--format", out var format);
    var geometry = Geometry(format);
    if (format is not null && geometry is null) { Console.Error.WriteLine($"** FATAL ERROR: Unknown format '{format}'"); return 1; }
    var (cyls, heads, spt, name) = geometry ?? (84, 2, 0, "Raw Flux");
    var raw = format is null || options.ContainsKey("--raw");
    var retries = options.TryGetValue("--retries", out var r) ? int.Parse(r, CultureInfo.InvariantCulture) : 3;

    Console.WriteLine($"[{Marker}] Reading c=0-{cyls - 1}:h=0-{heads - 1} revs={options.GetValueOrDefault("--revs", "2")}");
    if (format is not null) Console.WriteLine($"Format {format}");

    int found = 0, total = 0;
    for (var c = 0; c < cyls; c++)
    for (var h = 0; h < heads; h++)
    {
        Thread.Sleep(delay);
        if (spt == 0)
        {
            Console.WriteLine($"T{c}.{h}: Raw Flux (150{c % 10}12 flux in 400.0{h}ms)");
            continue;
        }
        var n = SectorsFor(format!, c, spt);
        total += n;
        var good = n;
        if (scenario == "badtracks" && c == 40 && h == 1) good = n - 2;
        if (scenario == "badtracks" && c == 12 && h == 0)
        {
            Console.WriteLine($"T{c}.{h}: {name} ({n - 1}/{n} sectors) from Raw Flux (150312 flux in 400.04ms)");
            Console.WriteLine($"T{c}.{h}: Retry #1.1");
        }
        else if (good < n)
        {
            Console.WriteLine($"T{c}.{h}: {name} ({good}/{n} sectors) from Raw Flux (150312 flux in 400.04ms)");
            for (var i = 1; i <= retries; i++)
            {
                Thread.Sleep(delay);
                Console.WriteLine($"T{c}.{h}: Retry #{i}.1");
            }
            Console.WriteLine($"T{c}.{h}: Giving up: {n - good} sectors missing");
        }
        Console.WriteLine($"T{c}.{h}: {name} ({good}/{n} sectors) from Raw Flux (150312 flux in 400.04ms)");
        found += good;
        if (scenario == "fail" && c == 20)
        {
            Console.Error.WriteLine("** FATAL ERROR: Command Failed: GetFlux: No Index");
            return 1;
        }
    }

    // Placeholder bytes only — not a valid image. Size matches the expected container size.
    var bytes = raw ? 4 * 1024 * 1024 : found * 512 + (total - found) * 512;
    using (var fs = File.Create(output))
    {
        if (raw) fs.Write("SCP"u8);
        fs.SetLength(bytes);
    }
    if (spt > 0) Console.WriteLine($"Found {found} sectors of {total} ({100.0 * found / total:0.0}%)");
    return 0;
}

int Write()
{
    if (scenario == "nodevice") return Info();
    if (positional.Count != 1 || !File.Exists(positional[0])) { Console.Error.WriteLine("** FATAL ERROR: image not found"); return 1; }
    options.TryGetValue("--format", out var format);
    var (cyls, heads, _, _) = Geometry(format) ?? (84, 2, 0, "Raw Flux");
    if (format is not null) Console.WriteLine($"Format {format}");
    Console.WriteLine($"[{Marker}] Writing c=0-{cyls - 1}:h=0-{heads - 1}");
    for (var c = 0; c < cyls; c++)
    for (var h = 0; h < heads; h++)
    {
        Thread.Sleep(delay);
        // Matches gw 1.23's per-track write line.
        Console.WriteLine($"T{c}.{h}: Writing Track (Flux: 200.0ms period, 219.9 ms total, Terminate at index)");
        if (scenario == "fail" && c == 30)
        {
            Console.Error.WriteLine("** FATAL ERROR: Verify Failure: T30.0");
            return 1;
        }
    }
    if (!options.ContainsKey("--no-verify")) Console.WriteLine("All tracks verified");
    return 0;
}

static (int Cyls, int Heads, int Spt, string Name)? Geometry(string? format) => format switch
{
    "amiga.amigados" => (80, 2, 11, "Amiga DOS"),
    "atarist.720" => (80, 2, 9, "IBM MFM"),
    "atarist.360" => (80, 1, 9, "IBM MFM"),
    "ibm.1440" => (80, 2, 18, "IBM MFM"),
    "ibm.720" => (80, 2, 9, "IBM MFM"),
    "commodore.1541" => (35, 1, 21, "Commodore GCR"),
    _ => null,
};

static int SectorsFor(string format, int cyl, int spt) => format == "commodore.1541"
    ? cyl switch { < 17 => 21, < 24 => 19, < 30 => 18, _ => 17 }
    : spt;
