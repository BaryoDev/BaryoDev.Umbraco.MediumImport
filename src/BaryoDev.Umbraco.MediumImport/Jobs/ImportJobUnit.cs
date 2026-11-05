using BaryoDev.Umbraco.MediumImport.Engine;
using BaryoDev.Umbraco.MediumImport.Uploads;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Web;

namespace BaryoDev.Umbraco.MediumImport.Jobs;

/// <summary>One import run: opens the upload, makes a service scope and an Umbraco context, runs the engine and deletes the upload.</summary>
public sealed class ImportJobUnit(IServiceScopeFactory scopes, UploadStore uploads, IUmbracoContextFactory contexts)
{
    public async Task<ImportResult> RunAsync(ImportJobRequest request, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            using var _ = contexts.EnsureUmbracoContext();
            await using Stream zip = uploads.Open(request.UploadId);
            var engine = scope.ServiceProvider.GetRequiredService<MediumImportEngine>();
            return await engine.ImportAsync(zip, request.PostIds, request.Settings, request.UserId, progress, ct);
        }
        finally
        {
            uploads.Delete(request.UploadId);
        }
    }
}
