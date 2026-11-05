using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.MediumImport.Net;

/// <summary>
/// Downloads Medium images. https only, two Medium CDN hosts, redirects followed by hand (at most 3,
/// each target checked), image content types only, and a size cap enforced as the bytes arrive.
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

                return new FetchedImage(FileNameFor(current), bytes);
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

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeChars();
}
