using BaryoDev.Umbraco.MediumImport.Conversion;
using BaryoDev.Umbraco.MediumImport.Net;

namespace BaryoDev.Umbraco.MediumImport.Content;

public interface IMediaStore
{
    ResolvedImage Save(string postFolder, FetchedImage image, int userId);

    /// <summary>The media item for the key, or null when it is gone.</summary>
    ResolvedImage? Find(Guid mediaKey);
}
