namespace FluxForge.Application.Processes;

public enum OutputStream { StdOut, StdErr }

public sealed record OutputLine(OutputStream Stream, string Text, DateTimeOffset At);

/// <summary>
/// A subprocess invocation. Arguments are passed as a list and never concatenated into a shell string.
/// </summary>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    TimeSpan? Timeout = null);

public sealed record ProcessResult(
    int? ExitCode,
    IReadOnlyList<OutputLine> Lines,
    bool Cancelled,
    bool TimedOut,
    TimeSpan Duration)
{
    public bool Completed => !Cancelled && !TimedOut && ExitCode is not null;

    public string AllText => string.Join(Environment.NewLine, Lines.Select(l => l.Text));
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<OutputLine>? output, CancellationToken cancellationToken);
}
