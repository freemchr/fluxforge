using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Processes;
using FluxForge.Application.Tools;

namespace FluxForge.Application.Devices;

public sealed class DeviceService(IProcessRunner runner, JobRunner jobs)
{
    public static readonly TimeSpan InfoTimeout = TimeSpan.FromSeconds(20);

    public async Task<DeviceStatus> DetectAsync(HostToolsInstallation tools, string? port, CancellationToken ct)
    {
        if (jobs.IsDeviceBusy(port))
            return new DeviceStatus(DeviceState.Busy, null, "A job is using the Greaseweazle.");
        try
        {
            var result = await jobs.WithDeviceAsync(port, () =>
                runner.RunAsync(new ProcessRequest(tools.ExecutablePath, GwCommands.Info(new DeviceTarget(port)), Timeout: InfoTimeout), null, ct))
                .ConfigureAwait(false);
            return Classify(result);
        }
        catch (DeviceBusyException e)
        {
            return new DeviceStatus(DeviceState.Busy, null, e.Message);
        }
    }

    public static DeviceStatus Classify(ProcessResult result)
    {
        if (result.Cancelled) return DeviceStatus.Unknown;
        if (result.TimedOut)
            return new DeviceStatus(DeviceState.Error, null, "gw info did not respond. Unplug and reconnect the board, then try again.");

        var info = GwOutputParser.ParseInfo(result.AllText);
        var text = result.AllText;
        if (info.DeviceFound && result.ExitCode == 0)
            return new DeviceStatus(DeviceState.Connected, info,
                $"{info.Model} on {info.Port ?? "auto-detected port"}" + (info.Firmware is { } fw ? $", firmware {fw}" : ""));
        if (Has(text, "Access is denied") || Has(text, "PermissionError") || Has(text, "could not open port"))
            return new DeviceStatus(DeviceState.Busy, info, "The serial port is in use by another program. Close other Greaseweazle tools and retry.");
        if (Has(text, "firmware") && (Has(text, "update") || Has(text, "incompatible") || Has(text, "unsupported")))
            return new DeviceStatus(DeviceState.Error, info,
                "The board firmware may not be compatible with these host tools. Review the firmware assistant before updating anything.");
        return new DeviceStatus(DeviceState.Disconnected, info,
            "No Greaseweazle detected. Check the USB cable and that the board appears as a COM port in Device Manager.");
    }

    private static bool Has(string text, string needle) => text.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
