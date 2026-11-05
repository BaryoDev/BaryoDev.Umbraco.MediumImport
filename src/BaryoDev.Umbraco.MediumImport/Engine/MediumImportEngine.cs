using BaryoDev.Umbraco.MediumImport.Content;
using BaryoDev.Umbraco.MediumImport.Conversion;
using BaryoDev.Umbraco.MediumImport.Identity;
using BaryoDev.Umbraco.MediumImport.Net;
using BaryoDev.Umbraco.MediumImport.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Scoping;

namespace BaryoDev.Umbraco.MediumImport.Engine;

public sealed class MediumImportEngine
{
    private readonly IStoryKeyRepository _stories;
    private readonly IMediaKeyRepository _media;
    private readonly IImageFetcher _images;
    private readonly IGistClient _gists;
    private readonly IPostWriter _writer;
    private readonly IMediaStore _store;
    private readonly IReadOnlyList<IMediumStoryFilter> _filters;
    private readonly IMediumSlugPolicy? _slugs;
    private readonly IReadOnlyList<IMediumImportCallback> _callbacks;
    private readonly MediumImportOptions _options;
    private readonly ILogger<MediumImportEngine>? _logger;
    private readonly ICoreScopeProvider? _scopes;

    public MediumImportEngine(
        IStoryKeyRepository stories,
        IMediaKeyRepository media,
        IImageFetcher images,
        IGistClient gists,
        IPostWriter writer,
        IMediaStore store,
        IEnumerable<IMediumStoryFilter> filters,
        IEnumerable<IMediumSlugPolicy> slugs,
        IEnumerable<IMediumImportCallback> callbacks,
        IOptions<MediumImportOptions> options,
        ILogger<MediumImportEngine>? logger = null,
        ICoreScopeProvider? scopes = null)
    {
        _stories = stories;
        _media = media;
        _images = images;
        _gists = gists;
        _writer = writer;
        _store = store;
        _filters = filters.ToList();
        _slugs = slugs.FirstOrDefault();
        _callbacks = callbacks.ToList();
        _options = options.Value;
        _logger = logger;
        _scopes = scopes;
    }

    public async Task<PreviewResult> PreviewAsync(Stream zip, ImportSettings s, CancellationToken ct)
    {
        IReadOnlyList<MediumStory> stories = await ReadAsync(zip, ct);
        var list = new List<PreviewStory>();
        foreach (MediumStory story in stories)
        {
            (StoryStatus status, string? reason, Guid? key, _) = Classify(story, s, _stories.Get(story.PostId));
            list.Add(new PreviewStory(story.PostId, Title(story), story.Published, story.Images.Count, story.GistIds.Count, status, reason, key));
        }

        return new PreviewResult(list, stories.Count(x => x.IsDraft), stories.Count(x => x.IsResponse));
    }

    public async Task<ImportResult> ImportAsync(
        Stream zip,
        IReadOnlyCollection<string> postIds,
        ImportSettings s,
        int userId,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        IReadOnlyList<MediumStory> all = await ReadAsync(zip, ct);
        var wanted = new HashSet<string>(postIds, StringComparer.Ordinal);
        List<MediumStory> selected = all.Where(x => wanted.Contains(x.PostId)).ToList();

        var gistCache = new Dictionary<string, Gist?>(StringComparer.Ordinal);
        var imageCache = new Dictionary<Uri, ResolvedImage?>();
        var results = new List<StoryResult>();
        int done = 0;
        bool cancelled = false;
        foreach (MediumStory story in selected)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            StoryResult result;
            try
            {
                result = await ImportOneAsync(story, s, userId, _stories.Get(story.PostId), gistCache, imageCache, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Story {PostId} failed to import", story.PostId);
                result = new StoryResult(story.PostId, Title(story), Outcome.Failed, ex.Message, null, [], [ex.Message]);
            }

            foreach (IMediumImportCallback callback in _callbacks)
            {
                try
                {
                    await callback.OnStoryAsync(story, result, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "A callback failed for story {PostId}", story.PostId);
                    result = result with { Warnings = [.. result.Warnings, "A callback failed: " + ex.Message] };
                }
            }

            results.Add(result);
            done++;
            progress?.Report(new ImportProgress(done, selected.Count, Title(story)));
            if (cancelled)
            {
                break;
            }
        }

        var warnings = new List<string>();
        var run = new ImportResult(results) { Cancelled = cancelled };
        foreach (IMediumImportCallback callback in _callbacks)
        {
            try
            {
                await callback.OnRunCompletedAsync(run, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "A callback failed on run completion");
                warnings.Add("A callback failed: " + ex.Message);
            }
        }

        return run with { Warnings = warnings };
    }

    private async Task<IReadOnlyList<MediumStory>> ReadAsync(Stream zip, CancellationToken ct)
    {
        MemoryStream? copy = null;
        try
        {
            Stream source = zip;
            if (!zip.CanSeek)
            {
                copy = await CopyCappedAsync(zip, _options.MaxUploadBytes, ct);
                source = copy;
            }

            IReadOnlyList<MediumStory> read = MediumExportReader.Read(source, MediumExportLimits.From(_options));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return read.Where(x => seen.Add(x.PostId)).ToList();
        }
        finally
        {
            copy?.Dispose();
        }
    }

    private static async Task<MemoryStream> CopyCappedAsync(Stream input, long cap, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var buffer = new byte[81920];
        try
        {
            int n;
            while ((n = await input.ReadAsync(buffer, ct)) > 0)
            {
                if (ms.Length + n > cap)
                {
                    throw new MediumExportException("The upload is too large.");
                }

                ms.Write(buffer, 0, n);
            }
        }
        catch (IOException ex)
        {
            ms.Dispose();
            throw new MediumExportException("The upload could not be read: " + ex.Message);
        }
        catch
        {
            ms.Dispose();
            throw;
        }

        ms.Position = 0;
        return ms;
    }

    private static bool IsMediumHttps(Uri? url) =>
        url is { IsAbsoluteUri: true } && url.Scheme == Uri.UriSchemeHttps
        && (url.Host.Equals("medium.com", StringComparison.OrdinalIgnoreCase)
            || url.Host.EndsWith(".medium.com", StringComparison.OrdinalIgnoreCase));

    private static string Title(MediumStory story) =>
        string.IsNullOrWhiteSpace(story.Title) ? story.PostId : story.Title;

    private (StoryStatus Status, string? Reason, Guid? ContentKey, string? Hash) Classify(MediumStory story, ImportSettings s, StoryKeyRow? row)
    {
        if (story.IsDraft)
        {
            return (StoryStatus.Draft, "Draft", null, null);
        }

        if (story.IsResponse)
        {
            return (StoryStatus.Response, "Response to another post", null, null);
        }

        foreach (IMediumStoryFilter filter in _filters)
        {
            string? reason = filter.Reject(story);
            if (reason is not null)
            {
                return (StoryStatus.Filtered, reason, null, null);
            }
        }

        if (row is not null)
        {
            IReadOnlyDictionary<string, string?>? mapped = _writer.ReadMapped(row.ContentKey, s);
            if (mapped is null)
            {
                return (StoryStatus.New, null, null, null);
            }

            string current = ValuesHash.Compute(mapped);
            return current == row.ValuesHash
                ? (StoryStatus.AlreadyImported, null, row.ContentKey, current)
                : (StoryStatus.EditedSinceImport, "Edited since import", row.ContentKey, current);
        }

        if (s.Mapping.OriginalUrl is not null && IsMediumHttps(story.MediumUrl))
        {
            Guid? found = _writer.FindByOriginalUrl(s, story.MediumUrl!.AbsoluteUri);
            if (found is not null)
            {
                return (StoryStatus.AlreadyImported, "Found by original url", found, null);
            }
        }

        return (StoryStatus.New, null, null, null);
    }

    private async Task<StoryResult> ImportOneAsync(
        MediumStory story,
        ImportSettings s,
        int userId,
        StoryKeyRow? row,
        Dictionary<string, Gist?> gistCache,
        Dictionary<Uri, ResolvedImage?> imageCache,
        CancellationToken ct)
    {
        string title = Title(story);
        (StoryStatus status, string? reason, Guid? existing, _) = Classify(story, s, row);
        switch (status)
        {
            case StoryStatus.Draft or StoryStatus.Response or StoryStatus.Filtered:
                return new StoryResult(story.PostId, title, Outcome.Skipped, reason, null, [], []);
            case StoryStatus.EditedSinceImport:
                return new StoryResult(story.PostId, title, Outcome.Skipped, "Edited since import", existing, [], []);
            case StoryStatus.AlreadyImported when row is null:
                {
                    // Found by original url: the literal hash never matches, so reruns skip it as edited.
                    _stories.Upsert(new StoryKeyRow
                    {
                        PostId = story.PostId,
                        ContentKey = existing!.Value,
                        ValuesHash = "adopted",
                        ImportedUtc = DateTime.UtcNow,
                    });
                    return new StoryResult(story.PostId, title, Outcome.Skipped, "Already on the site", existing, [], []);
                }
        }

        var warnings = new List<string>();
        var failures = new List<string>();

        var gists = new Dictionary<string, Gist>(StringComparer.Ordinal);
        foreach (string id in story.GistIds)
        {
            if (!gistCache.TryGetValue(id, out Gist? gist))
            {
                gist = await _gists.GetAsync(id, ct);
                gistCache[id] = gist;
            }

            if (gist is null)
            {
                warnings.Add($"Gist {id} could not be fetched, its link stays as it is.");
            }
            else
            {
                gists[id] = gist;
            }
        }

        string folder = PostFolder(story, title);
        var images = new Dictionary<Uri, ResolvedImage>();
        ResolvedImage? cover = null;
        foreach (Uri url in story.Images)
        {
            if (!MediumHosts.IsMediumImage(url))
            {
                warnings.Add($"Image on another host was left as it is: {url}");
                continue;
            }

            if (!imageCache.TryGetValue(url, out ResolvedImage? resolved))
            {
                resolved = await ResolveImageAsync(url, folder, userId, ct);
                imageCache[url] = resolved;
            }

            if (resolved is null)
            {
                failures.Add($"Image could not be imported: {url}");
            }
            else
            {
                images[url] = resolved;
                // The cover is the first resolved Medium image; failed and other-host images are not candidates.
                cover ??= resolved;
            }
        }

        string body = PostHtmlSanitizer.Sanitize(story.BodyHtml, new SanitizeContext(images, gists));
        FieldMapping m = s.Mapping;
        string? medium = story.MediumUrl?.AbsoluteUri;
        var values = new Dictionary<string, object?> { [m.Title] = title, [m.Body] = body };
        if (m.PublishDate is not null)
        {
            values[m.PublishDate] = story.Published;
        }

        if (m.Summary is not null)
        {
            values[m.Summary] = story.Summary;
        }

        if (m.Tags is not null)
        {
            values[m.Tags] = story.Tags.ToArray();
        }

        if (m.Canonical is not null)
        {
            values[m.Canonical] = s.Canonical == CanonicalMode.KeepMedium ? medium : null;
        }

        if (m.OriginalUrl is not null)
        {
            values[m.OriginalUrl] = medium;
        }

        var post = new PostValues(title, _slugs?.SlugFor(story), values, m.Cover is null ? null : cover?.MediaKey);

        // The node and its key row succeed or fail together. If the publish throws, or the process dies
        // before Complete, the scope rolls the node back, so a rerun never sees a node without a row.
        // Nothing in this block awaits, so the ambient scope stays on this thread.
        Guid key;
        using (ICoreScope? scope = _scopes?.CreateCoreScope())
        {
            key = _writer.Write(s, post, existing, userId);
            IReadOnlyDictionary<string, string?> back = _writer.ReadMapped(key, s) ?? new Dictionary<string, string?>();
            SaveRow(story.PostId, key, back);
            scope?.Complete();
        }

        return new StoryResult(story.PostId, title, existing is null ? Outcome.Imported : Outcome.Updated, null, key, warnings, failures);
    }

    private void SaveRow(string postId, Guid contentKey, IReadOnlyDictionary<string, string?> mapped) =>
        _stories.Upsert(new StoryKeyRow
        {
            PostId = postId,
            ContentKey = contentKey,
            ValuesHash = ValuesHash.Compute(mapped),
            ImportedUtc = DateTime.UtcNow,
        });

    private async Task<ResolvedImage?> ResolveImageAsync(Uri url, string folder, int userId, CancellationToken ct)
    {
        Guid? known = _media.Get(url);
        if (known is not null)
        {
            ResolvedImage? found = _store.Find(known.Value);
            if (found is not null)
            {
                return found;
            }
        }

        FetchedImage? fetched = await _images.FetchAsync(url, ct);
        if (fetched is null)
        {
            return null;
        }

        ResolvedImage saved = _store.Save(folder, fetched, userId);
        _media.Upsert(url, saved.MediaKey);
        return saved;
    }

    private static string PostFolder(MediumStory story, string title)
    {
        string date = story.Published?.ToString("yyyy-MM-dd") ?? "undated";
        string name = $"{date} {title}";
        return name.Length > 100 ? name[..100] : name;
    }
}
