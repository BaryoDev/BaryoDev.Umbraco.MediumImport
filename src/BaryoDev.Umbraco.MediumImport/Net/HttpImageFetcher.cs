using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.MediumImport.Net;

/// <summary>
/// Downloads Medium images. https only, two Medium CDN hosts, redirects followed by hand (at most 3,
/// each target checked), image content types and JPEG, PNG, GIF or WebP bytes only, and a size cap enforced as the bytes arrive.
/// </summary>
public sealed partial class HttpImageFetcher : IImageFetcher
{
    public const string ClientName = "MediumImport";
    private const int MaxRedirects = 3;

    private readonly IHttpClientFactory _factory;
    private readonly MediumImportOptions _options;
    private readonly ILogger<HttpImageFetcher> _logger;

    public HttpImageFetcher(IHttpClientFactory factory, IOptions<MediumImportOptions> options, ILogger<HttpImageFetcher> logger)
    {
        _factory = factory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FetchedImage?> FetchAsync(Uri url, CancellationToken ct)
    {
        if (!MediumHosts.IsMediumImage(url))
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds));
            HttpClient http = _factory.CreateClient(ClientName);
            Uri current = url;
            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                using HttpResponseMessage res = await http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (IsRedirect(res.StatusCode))
                {
                    Uri? location = res.Headers.Location;
                    if (location is null || hop == MaxRedirects)
                    {
                        _logger.LogWarning("Skipped image {Url}: bad or too many redirects", url);
                        return null;
                    }

                    Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (!MediumHosts.IsMediumImage(next))
                    {
                        _logger.LogWarning("Skipped image {Url}: redirect to a non-Medium host", url);
                        return null;
                    }

                    current = next;
                    continue;
                }

                string? type = res.Content.Headers.ContentType?.MediaType;
                if (!res.IsSuccessStatusCode || type is null || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Skipped image {Url}: status {Status}, type {Type}", url, (int)res.StatusCode, type);
                    return null;
                }

                byte[]? bytes = await BoundedRead.ReadAsync(res.Content, _options.MaxImageBytes, cts.Token);
                if (bytes is null)
                {
                    _logger.LogWarning("Skipped image {Url}: over {Max} bytes", url, _options.MaxImageBytes);
                    return null;
                }

                // The bytes decide, not the header or the URL. SVG and anything else that is not a raster image is refused.
                string? ext = SniffExtension(bytes);
                if (ext is null)
                {
                    _logger.LogWarning("Skipped image {Url}: not a JPEG, PNG, GIF or WebP image", url);
                    return null;
                }

                return new FetchedImage(WithExtension(FileNameFor(current), ext), bytes);
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or UriFormatException or IOException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogWarning(ex, "Skipped image {Url}", url);
            return null;
        }
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    internal static string FileNameFor(Uri url)
    {
        string last = url.Segments.Length > 0 ? Uri.UnescapeDataString(url.Segments[^1]).Trim('/') : "";
        string safe = UnsafeChars().Replace(last, "_").Trim('.', '_');
        if (safe.Length > 100)
        {
            safe = safe[..100];
        }

        return safe.Length == 0 ? "medium-image" : safe;
    }

    private static readonly string[] KnownExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    /// <summary>Adds the sniffed extension when the name has no known raster one.</summary>
    internal static string WithExtension(string name, string sniffedExtension) =>
        KnownExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase) ? name : name + sniffedExtension;

    internal static string? SniffExtension(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
        {
            return ".jpg";
        }

        if (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
        {
            return ".png";
        }

        if (b.Length >= 4 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38)
        {
            return ".gif";
        }

        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
            && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50)
        {
            return ".webp";
        }

        return null;
    }

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeChars();
}
