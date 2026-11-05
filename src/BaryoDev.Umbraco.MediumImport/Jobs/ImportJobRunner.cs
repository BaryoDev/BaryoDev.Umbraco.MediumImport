using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.MediumImport.Jobs;

/// <summary>Background loop. It waits for Umbraco to be fully running, then runs queued import jobs one at a time.</summary>
public sealed class ImportJobRunner(
    ImportJobStore store, ImportJobUnit unit, IRuntimeState runtime, ILogger<ImportJobRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool worked = false;
            try
            {
                if (runtime.Level == RuntimeLevel.Run)
                {
                    ImportJob? job = store.Claim();
                    if (job is not null)
                    {
                        worked = true;
                        using CancellationTokenRegistration _ = stoppingToken.Register(() => job.Cts.Cancel());
                        await store.ExecuteAsync(job, unit.RunAsync);
                    }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "The Medium import runner hit an error and will try again.");
            }
            catch (OperationCanceledException e)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                logger.LogError(e, "The Medium import runner hit an error and will try again.");
            }

            if (!worked)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
