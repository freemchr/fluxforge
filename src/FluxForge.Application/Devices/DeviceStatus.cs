using FluxForge.Application.Gw;

namespace FluxForge.Application.Devices;

public enum DeviceState { Unknown, Connected, Disconnected, DriverRequired, Busy, Error }

public sealed record DeviceStatus(DeviceState State, GwInfo? Info, string Message)
{
    public static DeviceStatus Unknown { get; } = new(DeviceState.Unknown, null, "Not checked yet");
}
