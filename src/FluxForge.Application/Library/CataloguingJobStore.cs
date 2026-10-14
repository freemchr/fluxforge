using System.Diagnostics;
using FluxForge.Application.Jobs;
using FluxForge.Application.Processes;

namespace FluxForge.Application.Library;

/// <summary>
/// Saves the job as usual, then adds what it produced to the Library. The job files stay the source of
/// truth: a catalogue problem is never allowed to fail or lose a job.
/// </summary>
public sealed class CataloguingJobStore(IJobStore inner, LibraryService library) : IJobStore
{
    public string JobsDirectory => inner.JobsDirectory;

    public async Task<JobRecord> SaveAsync(JobRecord record, IReadOnlyList<OutputLine> log, CancellationToken ct)
    {
        var saved = await inner.SaveAsync(record, log, ct).ConfigureAwait(false);
        try { library.Record(saved); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Trace.TraceWarning($"Library: could not catalogue job {saved.Id}: {e.Message}");
        }
        return saved;
    }

    public IReadOnlyList<JobRecord> List(int max = 200) => inner.List(max);
}
