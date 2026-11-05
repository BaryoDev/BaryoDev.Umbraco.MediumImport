using System.Text.Json;
using System.Text.RegularExpressions;
using BaryoDev.Umbraco.MediumImport.Conversion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.MediumImport.Net;

/// <summary>Reads public gists from api.github.com only.</summary>
public sealed partial class GistClient : IGistClient
{
    private readonly IHttpClientFactory _factory;
    private readonly MediumImportOptions _options;
    private readonly ILogger<GistClient> _logger;

    public GistClient(IHttpClientFactory factory, IOptions<MediumImportOptions> options, ILogger<GistClient> logger)
    {
        _factory = factory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Gist?> GetAsync(string gistId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(gistId) || !GistId().IsMatch(gistId))
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds));
            HttpClient http = _factory.CreateClient(HttpImageFetcher.ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/gists/" + gistId);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage res = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gist {Id} skipped: status {Status}", gistId, (int)res.StatusCode);
                return null;
            }

            byte[]? body = await BoundedRead.ReadAsync(res.Content, _options.MaxGistBytes, cts.Token);
            if (body is null)
            {
                _logger.LogWarning("Gist {Id} skipped: over {Max} bytes", gistId, _options.MaxGistBytes);
                return null;
            }

            return Map(gistId, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or IOException or ArgumentException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogWarning(ex, "Gist {Id} skipped", gistId);
            return null;
        }
    }

    private Gist? Map(string gistId, byte[] body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("files", out JsonElement files)
            || files.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var mapped = new List<GistFile>();
        foreach (JsonProperty entry in files.EnumerateObject())
        {
            JsonElement f = entry.Value;
            if (f.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (f.TryGetProperty("truncated", out JsonElement truncated) && truncated.ValueKind == JsonValueKind.True)
            {
                _logger.LogWarning("Gist {Id} skipped: file {File} is truncated", gistId, entry.Name);
                return null;
            }

            string name = Str(f, "filename") ?? entry.Name;
            string? content = Str(f, "content");
            if (content is null)
            {
                return null;
            }

            string? language = CodeLanguage.Normalize(Str(f, "language")) ?? CodeLanguage.FromFileName(name);
            mapped.Add(new GistFile(name, language, content));
        }

        Uri htmlUrl = Uri.TryCreate(Str(root, "html_url"), UriKind.Absolute, out Uri? u)
            && u.Scheme == Uri.UriSchemeHttps
            && u.Host.Equals("gist.github.com", StringComparison.OrdinalIgnoreCase)
            ? u
            : new Uri("https://gist.github.com/" + gistId);
        return new Gist(gistId, htmlUrl, mapped);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [GeneratedRegex("^[0-9a-f]{1,64}\\z")]
    private static partial Regex GistId();
}
