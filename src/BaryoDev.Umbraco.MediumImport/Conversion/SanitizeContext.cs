namespace BaryoDev.Umbraco.MediumImport.Conversion;

/// <summary>A Medium image that now lives in the media library.</summary>
public sealed record ResolvedImage(string Url, Guid MediaKey);

public sealed record GistFile(string Name, string? Language, string Content);

public sealed record Gist(string Id, Uri HtmlUrl, IReadOnlyList<GistFile> Files);

/// <summary>What the sanitizer needs to write media images and gist code blocks.</summary>
public sealed record SanitizeContext(
    IReadOnlyDictionary<Uri, ResolvedImage> Images,
    IReadOnlyDictionary<string, Gist> Gists);
