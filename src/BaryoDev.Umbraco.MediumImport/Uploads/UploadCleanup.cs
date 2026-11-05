using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.MediumImport.Uploads;

/// <summary>Every 10 minutes, deletes upload files older than <see cref="MediumImportOptions.UploadTtl"/>.</summary>
public sealed class UploadCleanup(
    UploadStore uploads, IOptions<MediumImportOptions> options, TimeProvider time, ILogger<UploadCleanup> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            do
            {
                try
                {
                    Sweep();
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Cleaning old Medium import uploads failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal int Sweep()
    {
        if (!Directory.Exists(uploads.Root))
        {
            return 0;
        }

        DateTime cutoff = time.GetUtcNow().UtcDateTime - options.Value.UploadTtl;
        int deleted = 0;
        foreach (string file in Directory.EnumerateFiles(uploads.Root))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }
}
