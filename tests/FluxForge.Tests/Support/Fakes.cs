using FluxForge.Application.Processes;

namespace FluxForge.Tests.Support;

internal sealed class FakeProcessRunner(Func<ProcessRequest, IProgress<OutputLine>?, CancellationToken, Task<ProcessResult>> handler) : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<OutputLine>? output, CancellationToken cancellationToken)
    {
        lock (Requests) Requests.Add(request);
        return handler(request, output, cancellationToken);
    }

    public static ProcessResult Result(int? exit, params string[] stdout) =>
        new(exit, [.. stdout.Select(l => new OutputLine(OutputStream.StdOut, l, DateTimeOffset.Now))], false, false, TimeSpan.FromSeconds(1));

    public static ProcessResult Emit(IProgress<OutputLine>? output, int exit, params string[] stdout)
    {
        var result = Result(exit, stdout);
        foreach (var line in result.Lines) output?.Report(line);
        return result;
    }
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("fluxforge-tests-").FullName;

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
