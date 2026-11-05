using BaryoDev.Umbraco.MediumImport.Content;
using BaryoDev.Umbraco.MediumImport.Conversion;
using BaryoDev.Umbraco.MediumImport.Engine;
using BaryoDev.Umbraco.MediumImport.Identity;
using BaryoDev.Umbraco.MediumImport.Net;
using BaryoDev.Umbraco.MediumImport.Parsing;

namespace BaryoDev.Umbraco.MediumImport.Tests.Fakes;

public sealed class FakePostWriter : IPostWriter
{
    public Dictionary<Guid, Dictionary<string, object?>> Nodes { get; } = new();
    public Dictionary<Guid, PostValues> Last { get; } = new();
    public Dictionary<string, Guid> ByOriginalUrl { get; } = new();
    public int Creates { get; private set; }
    public string? ThrowForName { get; set; }

    public Guid? FindByOriginalUrl(ImportSettings s, string url) =>
        ByOriginalUrl.TryGetValue(url, out Guid k) ? k : null;

    public IReadOnlyDictionary<string, string?>? ReadMapped(Guid contentKey, FieldMapping m)
    {
        if (!Nodes.TryGetValue(contentKey, out var node))
        {
            return null;
        }

        var result = new Dictionary<string, string?>();
        foreach (string? alias in new[] { m.Title, m.Body, m.PublishDate, m.Summary, m.Cover, m.Tags, m.Canonical, m.OriginalUrl })
        {
            if (alias is null)
            {
                continue;
            }

            node.TryGetValue(alias, out object? v);
            result[alias] = v switch
            {
                string[] a => string.Join(",", a),
                null => null,
                _ => v.ToString(),
            };
        }

        return result;
    }

    public Guid Write(ImportSettings s, PostValues v, Guid? existing, int userId)
    {
        if (v.Name == ThrowForName)
        {
            throw new InvalidOperationException("writer failed for " + v.Name);
        }

        Guid key = existing ?? Guid.NewGuid();
        if (existing is null)
        {
            Creates++;
        }

        Nodes[key] = new Dictionary<string, object?>(v.Values);
        Last[key] = v;
        return key;
    }
}

public sealed class FakeMediaStore : IMediaStore
{
    private readonly Dictionary<Guid, ResolvedImage> _items = new();
    public int Saves { get; private set; }
    public List<string> Folders { get; } = [];

    public ResolvedImage Save(string postFolder, FetchedImage image, int userId)
    {
        Saves++;
        Folders.Add(postFolder);
        var key = Guid.NewGuid();
        var item = new ResolvedImage("/media/" + key + "/" + image.FileName, key);
        _items[key] = item;
        return item;
    }

    public ResolvedImage? Find(Guid mediaKey) => _items.GetValueOrDefault(mediaKey);
}

public sealed class FakeImageFetcher : IImageFetcher
{
    public HashSet<string> Failing { get; } = [];
    public List<Uri> Requests { get; } = [];

    public Task<FetchedImage?> FetchAsync(Uri url, CancellationToken ct)
    {
        Requests.Add(url);
        FetchedImage? result = Failing.Contains(url.AbsoluteUri) ? null : new FetchedImage("pic.png", [1, 2, 3]);
        return Task.FromResult(result);
    }
}

public sealed class FakeGistClient : IGistClient
{
    public Dictionary<string, Gist> Known { get; } = new();
    public List<string> Requests { get; } = [];

    public Task<Gist?> GetAsync(string gistId, CancellationToken ct)
    {
        Requests.Add(gistId);
        return Task.FromResult(Known.GetValueOrDefault(gistId));
    }
}

public sealed class MemoryStoryKeys : IStoryKeyRepository
{
    public Dictionary<string, StoryKeyRow> Rows { get; } = new();

    public StoryKeyRow? Get(string postId) => Rows.GetValueOrDefault(postId);

    public void Upsert(StoryKeyRow row) => Rows[row.PostId] = row;
}

public sealed class MemoryMediaKeys : IMediaKeyRepository
{
    public Dictionary<Uri, Guid> Rows { get; } = new();

    public Guid? Get(Uri source) => Rows.TryGetValue(source, out Guid k) ? k : null;

    public void Upsert(Uri source, Guid mediaKey) => Rows[source] = mediaKey;
}

public sealed class CallbackSpy : IMediumImportCallback
{
    public List<string> Stories { get; } = [];
    public int Completed { get; private set; }
    public Action<StoryResult>? AfterStory { get; set; }

    public Task OnStoryAsync(MediumStory story, StoryResult result, CancellationToken ct)
    {
        Stories.Add(story.PostId);
        AfterStory?.Invoke(result);
        return Task.CompletedTask;
    }

    public Task OnRunCompletedAsync(ImportResult result, CancellationToken ct)
    {
        Completed++;
        return Task.CompletedTask;
    }
}

public sealed class RejectFilter(string postId, string reason) : IMediumStoryFilter
{
    public string? Reject(MediumStory story) => story.PostId == postId ? reason : null;
}

public sealed class FixedSlug(string slug) : IMediumSlugPolicy
{
    public string? SlugFor(MediumStory story) => slug;
}
