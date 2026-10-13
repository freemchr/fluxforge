using System.Diagnostics;
using System.Text;
using FluxForge.Application.Processes;

namespace FluxForge.Infrastructure.Processes;

/// <summary>
/// Runs gw.exe with an argument list (never a shell string), streams stdout/stderr line by line, and
/// kills the whole process tree on cancellation or timeout.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<OutputLine>? output, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = request.WorkingDirectory ?? Path.GetDirectoryName(request.FileName) ?? "",
        };
        foreach (var arg in request.Arguments)
            psi.ArgumentList.Add(arg);
        // gw.exe is a frozen Python app: without this, redirected stdout is block-buffered and live progress stalls.
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        var lines = new List<OutputLine>();
        void OnLine(OutputStream stream, string? text)
        {
            if (text is null) return;
            var line = new OutputLine(stream, text, DateTimeOffset.Now);
            lock (lines) lines.Add(line);
            output?.Report(line);
        }

        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnLine(OutputStream.StdOut, e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(OutputStream.StdErr, e.Data);

        try
        {
            if (!process.Start())
                return Failed($"Could not start {request.FileName}.");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return Failed($"Could not start {request.FileName}: {e.Message}");
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = request.Timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        bool cancelled = false, timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            process.WaitForExit(); // flush remaining async output events
        }
        catch (OperationCanceledException)
        {
            cancelled = cancellationToken.IsCancellationRequested;
            timedOut = !cancelled;
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        List<OutputLine> snapshot;
        lock (lines) snapshot = [.. lines];
        return new ProcessResult(cancelled || timedOut ? null : process.ExitCode, snapshot, cancelled, timedOut, sw.Elapsed);

        ProcessResult Failed(string message) =>
            new(-1, [new OutputLine(OutputStream.StdErr, message, DateTimeOffset.Now)], false, false, sw.Elapsed);
    }
}
