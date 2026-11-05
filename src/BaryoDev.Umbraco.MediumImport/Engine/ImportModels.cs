using BaryoDev.Umbraco.MediumImport.Parsing;

namespace BaryoDev.Umbraco.MediumImport.Engine;

public enum CanonicalMode { ThisSite, KeepMedium }

public sealed record FieldMapping(
    string Title,
    string Body,
    string? PublishDate,
    string? Summary,
    string? Cover,
    string? Tags,
    string? Canonical,
    string? OriginalUrl);

public sealed record ImportSettings(
    Guid ParentKey,
    string DocTypeAlias,
    FieldMapping Mapping,
    bool Publish = false,
    CanonicalMode Canonical = CanonicalMode.ThisSite);

public enum StoryStatus { New, AlreadyImported, EditedSinceImport, Draft, Response, Filtered }

public sealed record PreviewStory(
    string PostId,
    string Title,
    DateTime? Published,
    int ImageCount,
    int GistCount,
    StoryStatus Status,
    string? Reason,
    Guid? ContentKey);

public sealed record PreviewResult(IReadOnlyList<PreviewStory> Stories, int Drafts, int Responses);

public enum Outcome { Imported, Updated, Skipped, Failed }

public sealed record StoryResult(
    string PostId,
    string Title,
    Outcome Outcome,
    string? Reason,
    Guid? ContentKey,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Failures);

public sealed record ImportResult(IReadOnlyList<StoryResult> Stories);

public sealed record ImportProgress(int Done, int Total, string? Current);

public interface IMediumStoryFilter
{
    /// <summary>A reason to leave the story out, or null to keep it.</summary>
    string? Reject(MediumStory story);
}

public interface IMediumSlugPolicy
{
    /// <summary>The url segment for the story, or null to let Umbraco derive it from the name.</summary>
    string? SlugFor(MediumStory story);
}

public interface IMediumImportCallback
{
    Task OnStoryAsync(MediumStory story, StoryResult result, CancellationToken ct);

    Task OnRunCompletedAsync(ImportResult result, CancellationToken ct);
}
