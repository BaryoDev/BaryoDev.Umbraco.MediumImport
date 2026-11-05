using BaryoDev.Umbraco.MediumImport.Engine;

namespace BaryoDev.Umbraco.MediumImport.Jobs;

/// <summary>Holds import jobs in memory. One job may be Queued or Running at a time.</summary>
public sealed class ImportJobStore
{
    private const int KeepFinished = 20;

    private readonly object _gate = new();
    private readonly List<ImportJob> _jobs = [];

    /// <summary>Queues a job, or returns null when one is already Queued or Running.</summary>
    public ImportJob? TryStart(ImportJobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_jobs.Any(j => j.State is JobState.Queued or JobState.Running))
            {
                return null;
            }

            var job = new ImportJob(Guid.NewGuid(), request);
            _jobs.Add(job);
            List<ImportJob> finished = _jobs.Where(j => j.IsFinished).ToList();
            foreach (ImportJob old in finished.Take(Math.Max(0, finished.Count - KeepFinished)))
            {
                _jobs.Remove(old);
                old.Cts.Dispose();
            }

            return job;
        }
    }

    public ImportJob? Get(Guid id)
    {
        lock (_gate)
        {
            return _jobs.FirstOrDefault(j => j.Id == id);
        }
    }

    /// <summary>Cancels a Queued job at once, or asks a Running job to stop between stories. False when there is nothing to cancel.</summary>
    public bool Cancel(Guid id)
    {
        lock (_gate)
        {
            ImportJob? job = _jobs.FirstOrDefault(j => j.Id == id);
            if (job is null || job.IsFinished)
            {
                return false;
            }

            if (job.State == JobState.Queued)
            {
                job.State = JobState.Cancelled;
            }

            job.Cts.Cancel();
            return true;
        }
    }

    /// <summary>The oldest Queued request, which is marked Running.</summary>
    public ImportJobRequest? NextQueued() => Claim()?.Request;

    internal ImportJob? Claim()
    {
        lock (_gate)
        {
            ImportJob? job = _jobs.FirstOrDefault(j => j.State == JobState.Queued);
            if (job is not null)
            {
                job.State = JobState.Running;
            }

            return job;
        }
    }

    /// <summary>Runs a claimed job through <paramref name="run"/> and records how it ended.</summary>
    internal async Task ExecuteAsync(
        ImportJob job,
        Func<ImportJobRequest, IProgress<ImportProgress>, CancellationToken, Task<ImportResult>> run)
    {
        try
        {
            ImportResult result = await run(job.Request, new Reporter(job), job.Cts.Token);
            job.Result = result;
            job.State = result.Cancelled ? JobState.Cancelled : JobState.Completed;
        }
        catch (OperationCanceledException) when (job.Cts.IsCancellationRequested)
        {
            job.State = JobState.Cancelled;
        }
        catch (Exception ex)
        {
            job.Error = ex.Message;
            job.State = JobState.Failed;
        }
    }

    private sealed class Reporter(ImportJob job) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => job.Progress = value;
    }
}
