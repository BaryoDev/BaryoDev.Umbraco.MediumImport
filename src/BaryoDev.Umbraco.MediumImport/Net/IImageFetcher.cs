namespace BaryoDev.Umbraco.MediumImport.Net;

public sealed record FetchedImage(string FileName, byte[] Bytes);

public interface IImageFetcher
{
    /// <summary>Downloads a Medium image. Null on refusal or failure.</summary>
    Task<FetchedImage?> FetchAsync(Uri url, CancellationToken ct);
}
