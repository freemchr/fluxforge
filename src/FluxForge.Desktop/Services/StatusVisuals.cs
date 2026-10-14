using System.Windows.Media;
using FluxForge.Application.Devices;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Tools;

namespace FluxForge.Desktop.Services;

public sealed record StatusVisual(string Label, string Glyph, string BrushKey)
{
    public Brush Brush => (System.Windows.Application.Current.TryFindResource(BrushKey) as Brush) ?? Brushes.Gray;
}

/// <summary>One place that maps every status enum to label + glyph + colour, so colour is never the only signal.</summary>
public static class StatusVisuals
{
    public static StatusVisual For(object? value) => value switch
    {
        TrackStatus.NotRead => new("Not read", "", "FfEmptyCellBrush"),
        TrackStatus.Reading => new("Reading / unresolved", "…", "FfAccentBrush"),
        TrackStatus.DecodedOk => new("Decoded OK", "✓", "FfSuccessBrush"),
        TrackStatus.RecoveredAfterRetry => new("Recovered after retry", "R", "FfWarningBrush"),
        TrackStatus.Unreadable => new("Unreadable / missing sectors", "✕", "FfErrorBrush"),
        TrackStatus.FluxCapturedDecodeUnknown => new("Flux captured, decode unknown", "~", "FfInfoBrush"),
        TrackStatus.NotApplicable => new("Not applicable", "–", "FfNeutralBrush"),
        TrackStatus.ExtraSectors => new("Extra sectors outside the format", "+", "FfWarningBrush"),
        TrackStatus.Writing => new("Writing", "…", "FfAccentBrush"),
        TrackStatus.Written => new("Written, not yet verified", "W", "FfInfoBrush"),
        TrackStatus.Verified => new("Written and verified", "✓", "FfSuccessBrush"),
        TrackStatus.Match => new("Matches the image", "=", "FfSuccessBrush"),
        TrackStatus.Mismatch => new("Differs from the image", "≠", "FfErrorBrush"),
        TrackStatus.NotInImage => new("Image has no data to compare", "?", "FfWarningBrush"),

        JobStatus.Pending => new("Pending", "•", "FfNeutralBrush"),
        JobStatus.Running => new("Running", "…", "FfAccentBrush"),
        JobStatus.Succeeded => new("Succeeded", "✓", "FfSuccessBrush"),
        JobStatus.Partial => new("Partial", "!", "FfWarningBrush"),
        JobStatus.CompletedUnverified => new("Completed — unverified", "?", "FfInfoBrush"),
        JobStatus.Failed => new("Failed", "✕", "FfErrorBrush"),
        JobStatus.Cancelled => new("Cancelled", "■", "FfNeutralBrush"),
        JobStatus.Interrupted => new("Interrupted — disk state unknown", "!", "FfErrorBrush"),

        HostToolsState.Unknown => new("Checking", "…", "FfNeutralBrush"),
        HostToolsState.Found => new("Ready", "✓", "FfSuccessBrush"),
        HostToolsState.Missing => new("Missing", "✕", "FfWarningBrush"),
        HostToolsState.UpdateAvailable => new("Update available", "↑", "FfInfoBrush"),
        HostToolsState.Failed => new("Failed", "✕", "FfErrorBrush"),

        DeviceState.Unknown => new("Not checked", "…", "FfNeutralBrush"),
        DeviceState.Connected => new("Connected", "✓", "FfSuccessBrush"),
        DeviceState.Disconnected => new("Not detected", "✕", "FfWarningBrush"),
        DeviceState.DriverRequired => new("Driver required", "!", "FfWarningBrush"),
        DeviceState.Busy => new("Busy", "…", "FfInfoBrush"),
        DeviceState.Error => new("Problem", "!", "FfErrorBrush"),

        _ => new(value?.ToString() ?? "", "", "FfNeutralBrush"),
    };
}
