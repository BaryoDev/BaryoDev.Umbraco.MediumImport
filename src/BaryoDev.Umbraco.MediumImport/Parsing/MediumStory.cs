namespace BaryoDev.Umbraco.MediumImport.Parsing;

/// <summary>One post file from a Medium export. BodyHtml is raw and unsanitized, with the title and subtitle headings removed.</summary>
public sealed record MediumStory(
    string PostId,
    string FileName,
    string Title,
    string? Subtitle,
    string? Summary,
    DateTime? Published,
    IReadOnlyList<string> Tags,
    string BodyHtml,
    IReadOnlyList<Uri> Images,
    IReadOnlyList<string> GistIds,
    Uri? MediumUrl,
    bool IsDraft,
    bool IsResponse)
{
    /// <summary>Set when the file could not be read as a post (for example nested too deeply). The story must be reported as failed.</summary>
    public string? ParseFailure { get; init; }
}
