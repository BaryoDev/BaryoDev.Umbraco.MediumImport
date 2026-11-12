using BaryoDev.Umbraco.MediumImport.Engine;

namespace BaryoDev.Umbraco.MediumImport.Content;

public sealed record PostValues(
    string Name,
    string? UrlSegment,
    IReadOnlyDictionary<string, object?> Values,
    Guid? CoverMediaKey);

/// <remarks>
/// Posts are created and found as direct children of the parent. A post moved deeper is out of scope.
/// </remarks>
public interface IPostWriter
{
    /// <summary>Finds a post under the parent whose original url field equals the url.</summary>
    Guid? FindByOriginalUrl(ImportSettings settings, string url);

    /// <summary>
    /// Every post under the parent that has an original url, keyed by that url, read in one pass. Return null
    /// to make the engine call <see cref="FindByOriginalUrl"/> per story instead.
    /// </summary>
    IReadOnlyDictionary<string, Guid>? IndexByOriginalUrl(ImportSettings settings) => null;

    /// <summary>
    /// The mapped fields as strings, or null when the node is gone, not under settings.ParentKey,
    /// or not of settings.DocTypeAlias.
    /// </summary>
    IReadOnlyDictionary<string, string?>? ReadMapped(Guid contentKey, ImportSettings settings);

    /// <summary>Creates the post, or updates it when existing is set. An update keeps name, slug and publish state.</summary>
    Guid Write(ImportSettings settings, PostValues values, Guid? existing, int userId);
}
