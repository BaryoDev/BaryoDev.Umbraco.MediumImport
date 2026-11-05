using Asp.Versioning;
using BaryoDev.Umbraco.MediumImport.Content;
using BaryoDev.Umbraco.MediumImport.Engine;
using BaryoDev.Umbraco.MediumImport.Jobs;
using BaryoDev.Umbraco.MediumImport.Parsing;
using BaryoDev.Umbraco.MediumImport.Settings;
using BaryoDev.Umbraco.MediumImport.Uploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Authorization;

namespace BaryoDev.Umbraco.MediumImport.Api;

public sealed record UploadResponse(Guid UploadId);

public sealed record PreviewRequest(Guid UploadId, ImportSettings Settings);

public sealed record ImportRequest(Guid UploadId, IReadOnlyCollection<string> PostIds, ImportSettings Settings);

public sealed record StartedResponse(Guid JobId);

public sealed record ApiError(string Error);

public sealed record JobResponse(
    Guid Id, string State, int Done, int Total, string? Current, string? Error, ImportResult? Result);

/// <summary>Upload, preview and import for a Medium export. Settings section only, checked on the server.</summary>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("medium-import")]
[ApiExplorerSettings(GroupName = "Medium Import")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
[PlainServerErrors]
public sealed class MediumImportController(
    UploadStore uploads,
    ImportJobStore jobs,
    SettingsStore settings,
    MappingOptions options,
    MediumImportEngine engine,
    IBackOfficeSecurityAccessor security,
    IOptions<MediumImportOptions> importOptions,
    ILogger<MediumImportController> logger) : ManagementApiControllerBase
{
    public const string FailedMessage = "The import failed. The details are in the server log.";
    public const string StoryFailedMessage = "This story could not be imported. The details are in the server log.";
    public const string CallbackFailedMessage = "A callback failed. The details are in the server log.";
    public const string UnreadableMessage = "The export could not be read. Check that it is a Medium export zip.";

    private const long MultipartOverhead = 1024 * 1024;

    [HttpPost("upload")]
    [ProducesResponseType<UploadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        long cap = importOptions.Value.MaxUploadBytes;
        IHttpMaxRequestBodySizeFeature? limit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false })
        {
            limit.MaxRequestBodySize = cap + MultipartOverhead;
        }

        if (Request.ContentLength > cap + MultipartOverhead)
        {
            return TooLarge();
        }

        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out MediaTypeHeaderValue? type)
            || !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(type.Boundary).Value is not { Length: > 0 } boundary)
        {
            return BadRequest(new ApiError("Send the zip as multipart form data in a field named file."));
        }

        try
        {
            var reader = new MultipartReader(boundary, Request.Body);
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
            {
                if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out ContentDispositionHeaderValue? disposition)
                    && disposition.Name.Value?.Trim('"') == "file"
                    && disposition.FileName.HasValue)
                {
                    Guid id = await uploads.SaveAsync(section.Body, ct);
                    return Ok(new UploadResponse(id));
                }
            }
        }
        catch (UploadTooLargeException)
        {
            return TooLarge();
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }
        catch (InvalidDataException e)
        {
            logger.LogWarning(e, "The Medium import upload was not valid multipart data");
        }

        return BadRequest(new ApiError("Send the zip as multipart form data in a field named file."));
    }

    [HttpPost("preview")]
    [ProducesResponseType<PreviewResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest request, CancellationToken ct)
    {
        if (request?.Settings is null)
        {
            return BadRequest(new ApiError("Settings are required."));
        }

        if (!System.IO.File.Exists(uploads.PathOf(request.UploadId)))
        {
            return NotFound(new ApiError("upload"));
        }

        try
        {
            await using Stream zip = uploads.Open(request.UploadId);
            return Ok(await engine.PreviewAsync(zip, request.Settings, ct));
        }
        catch (MediumExportException e)
        {
            logger.LogWarning(e, "The Medium export could not be read");
            return BadRequest(new ApiError(UnreadableMessage));
        }
    }

    [HttpPost("import")]
    [ProducesResponseType<StartedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Import([FromBody] ImportRequest request)
    {
        if (request?.Settings is null || request.PostIds is null || request.PostIds.Count == 0)
        {
            return BadRequest(new ApiError("Settings and at least one post are required."));
        }

        if (security.BackOfficeSecurity?.CurrentUser?.Id is not { } userId)
        {
            return Unauthorized();
        }

        if (!System.IO.File.Exists(uploads.PathOf(request.UploadId)))
        {
            return NotFound(new ApiError("upload"));
        }

        settings.Save(request.Settings);
        ImportJob? job = jobs.TryStart(new ImportJobRequest(request.UploadId, request.PostIds, request.Settings, userId));
        if (job is null)
        {
            return Conflict(new ApiError("running"));
        }

        return Ok(new StartedResponse(job.Id));
    }

    [HttpGet("jobs/{id:guid}")]
    [ProducesResponseType<JobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public IActionResult GetJob(Guid id)
    {
        ImportJob? job = jobs.Get(id);
        if (job is null)
        {
            return NotFound(new ApiError("stopped"));
        }

        ImportProgress p = job.Progress;
        JobState state = job.State;
        return Ok(new JobResponse(
            job.Id,
            state.ToString(),
            p.Done,
            p.Total,
            p.Current,
            state == JobState.Failed ? FailedMessage : null,
            job.Result is { } r ? Safe(r) : null));
    }

    [HttpPost("jobs/{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Cancel(Guid id)
    {
        jobs.Cancel(id);
        return NoContent();
    }

    [HttpGet("settings")]
    [ProducesResponseType<ImportSettings>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult GetSettings() => settings.Load() is { } s ? Ok(s) : NoContent();

    [HttpPut("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult SaveSettings([FromBody] ImportSettings body)
    {
        if (body is null)
        {
            return BadRequest(new ApiError("Settings are required."));
        }

        settings.Save(body);
        return NoContent();
    }

    [HttpGet("options")]
    [ProducesResponseType<DocTypeOption[]>(StatusCodes.Status200OK)]
    public IActionResult Options([FromQuery] Guid parentKey) => Ok(options.For(parentKey).ToArray());

    private ObjectResult TooLarge() =>
        StatusCode(StatusCodes.Status413PayloadTooLarge, new ApiError("The file is larger than the upload limit."));

    /// <summary>Replaces text that may carry raw exception messages with fixed text.</summary>
    private static ImportResult Safe(ImportResult result) => result with
    {
        Stories = result.Stories.Select(s => s.Outcome == Outcome.Failed
            ? s with { Reason = StoryFailedMessage, Failures = [StoryFailedMessage] }
            : s with { Warnings = s.Warnings.Select(SafeWarning).ToList() }).ToList(),
        Warnings = result.Warnings.Select(SafeWarning).ToList(),
    };

    private static string SafeWarning(string w) =>
        w.StartsWith("A callback failed", StringComparison.Ordinal) ? CallbackFailedMessage : w;
}
