using BaryoDev.Umbraco.MediumImport.Engine;
using BaryoDev.Umbraco.MediumImport.Jobs;

namespace BaryoDev.Umbraco.MediumImport.Tests;

public class JobTests
{
    private static readonly ImportSettings Settings =
        new(Guid.NewGuid(), "post", new FieldMapping("title", "body", null, null, null, null, null, null));

    private static ImportJobRequest Request(params string[] ids) => new(Guid.NewGuid(), ids, Settings, 1);

    private static StoryResult Done(string id) => new(id, id, Outcome.Imported, null, Guid.NewGuid(), [], []);

    [Fact]
    public void Second_start_while_running_is_refused()
    {
        var store = new ImportJobStore();

        ImportJob? first = store.TryStart(Request("a"));
        Assert.NotNull(first);
        Assert.Null(store.TryStart(Request("b")));

        ImportJob claimed = store.Claim()!;
        Assert.Equal(JobState.Running, claimed.State);
        Assert.Null(store.TryStart(Request("b")));
    }

    [Fact]
    public async Task Start_is_allowed_again_once_the_job_has_finished()
    {
        var store = new ImportJobStore();
        ImportJob first = store.TryStart(Request("a"))!;
        await store.ExecuteAsync(store.Claim()!, (_, _, _) => Task.FromResult(new ImportResult([])));

        Assert.Equal(JobState.Completed, first.State);
        Assert.NotNull(store.TryStart(Request("b")));
        Assert.Same(first, store.Get(first.Id));
    }

    [Fact]
    public void Next_queued_hands_out_each_request_once()
    {
        var store = new ImportJobStore();
        ImportJobRequest request = Request("a");
        store.TryStart(request);

        Assert.Same(request, store.NextQueued());
        Assert.Null(store.NextQueued());
    }

    [Fact]
    public async Task Cancel_stops_between_stories()
    {
        var store = new ImportJobStore();
        ImportJob job = store.TryStart(Request("a", "b", "c"))!;
        ImportJob running = store.Claim()!;

        // Stands in for the engine: it checks the token before each story, as MediumImportEngine does.
        Task<ImportResult> Run(ImportJobRequest r, IProgress<ImportProgress> p, CancellationToken ct)
        {
            var done = new List<StoryResult>();
            bool cancelled = false;
            foreach (string id in r.PostIds)
            {
                if (ct.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                done.Add(Done(id));
                p.Report(new ImportProgress(done.Count, r.PostIds.Count, id));
                if (done.Count == 1)
                {
                    Assert.True(store.Cancel(job.Id));
                }
            }

            return Task.FromResult(new ImportResult(done) { Cancelled = cancelled });
        }

        await store.ExecuteAsync(running, Run);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Single(job.Result!.Stories);
        Assert.Equal(new ImportProgress(1, 3, "a"), job.Progress);
        Assert.False(store.Cancel(job.Id));
    }

    [Fact]
    public void Cancelling_a_queued_job_ends_it_without_running()
    {
        var store = new ImportJobStore();
        ImportJob job = store.TryStart(Request("a"))!;

        Assert.True(store.Cancel(job.Id));

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Null(store.Claim());
        Assert.False(store.Cancel(Guid.NewGuid()));
    }

    [Fact]
    public async Task Failed_unit_sets_failed_with_error()
    {
        var store = new ImportJobStore();
        ImportJob job = store.TryStart(Request("a"))!;

        await store.ExecuteAsync(store.Claim()!, (_, _, _) => throw new InvalidOperationException("zip is broken"));

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("zip is broken", job.Error);
        Assert.Null(job.Result);
    }

    [Fact]
    public async Task Cancel_that_surfaces_as_an_exception_ends_cancelled()
    {
        var store = new ImportJobStore();
        ImportJob job = store.TryStart(Request("a"))!;

        await store.ExecuteAsync(store.Claim()!, (_, _, ct) =>
        {
            store.Cancel(job.Id);
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ImportResult([]));
        });

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Null(job.Error);
    }
}
