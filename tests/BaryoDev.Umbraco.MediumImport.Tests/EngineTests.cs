using System.IO.Compression;
using System.Text;
using BaryoDev.Umbraco.MediumImport.Conversion;
using BaryoDev.Umbraco.MediumImport.Engine;
using BaryoDev.Umbraco.MediumImport.Parsing;
using BaryoDev.Umbraco.MediumImport.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.MediumImport.Tests;

public class EngineTests
{
    private const string Code = "0a1b2c3d4e5f";
    private const string Fast = "aa11bb22cc33";
    private const string Reply = "112233445566";
    private const string Skills = "bd4b225e0ec8";
    private const string Draft = "dd44ee55ff66";
    private const string GistId = "0123456789abcdef";

    private static readonly FieldMapping Mapping = new("title", "body", "date", "summary", "cover", "tags", "canonical", "orig");

    private sealed class Rig
    {
        public FakePostWriter Writer { get; } = new();
        public FakeMediaStore Store { get; } = new();
        public FakeImageFetcher Images { get; } = new();
        public FakeGistClient Gists { get; } = new();
        public MemoryStoryKeys Keys { get; } = new();
        public MemoryMediaKeys MediaKeys { get; } = new();
        public CallbackSpy Spy { get; } = new();
        public List<IMediumStoryFilter> Filters { get; } = [];
        public List<IMediumSlugPolicy> Slugs { get; } = [];
        public MediumImportOptions Options { get; } = new();

        public Rig()
        {
            Gists.Known[GistId] = new Gist(GistId, new Uri("https://gist.github.com/someone/" + GistId), [new GistFile("a.go", "go", "package main")]);
        }

        public MediumImportEngine Engine() =>
            new(Keys, MediaKeys, Images, Gists, Writer, Store, Filters, Slugs, [Spy], Microsoft.Extensions.Options.Options.Create(Options));
    }

    private static string Fixture(string prefix)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "posts");
        return Directory.GetFiles(dir, prefix + "*.html").Single();
    }

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static (string, string) Post(string prefix) =>
        ("posts/" + Path.GetFileName(Fixture(prefix)), File.ReadAllText(Fixture(prefix)));

    private static MemoryStream AllFixtures() =>
        Zip(Post("2024-01-02"), Post("2024-03-02"), Post("2024-04-10"), Post("2025-01-15"), Post("draft_"));

    private static ImportSettings Settings(CanonicalMode canonical = CanonicalMode.ThisSite, FieldMapping? mapping = null) =>
        new(Guid.NewGuid(), "blogPost", mapping ?? Mapping, false, canonical);

    private static string[] Ids(params string[] ids) => ids;

    private sealed class NonSeekable(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Preview_statuses_for_fixture_export()
    {
        var rig = new Rig();

        PreviewResult preview = await rig.Engine().PreviewAsync(AllFixtures(), Settings(), CancellationToken.None);

        Assert.Equal(1, preview.Drafts);
        Assert.Equal(1, preview.Responses);
        Assert.Equal(StoryStatus.Draft, preview.Stories.Single(x => x.PostId == Draft).Status);
        Assert.Equal(StoryStatus.Response, preview.Stories.Single(x => x.PostId == Reply).Status);
        Assert.Equal(StoryStatus.New, preview.Stories.Single(x => x.PostId == Code).Status);
        Assert.Equal(StoryStatus.New, preview.Stories.Single(x => x.PostId == Skills).Status);
        Assert.Equal(3, preview.Stories.Single(x => x.PostId == Code).ImageCount);
        Assert.Equal(1, preview.Stories.Single(x => x.PostId == Code).GistCount);
    }

    [Fact]
    public async Task Filter_reason_shows_in_preview()
    {
        var rig = new Rig();
        rig.Filters.Add(new RejectFilter(Skills, "internal"));

        PreviewResult preview = await rig.Engine().PreviewAsync(AllFixtures(), Settings(), CancellationToken.None);

        PreviewStory story = preview.Stories.Single(x => x.PostId == Skills);
        Assert.Equal(StoryStatus.Filtered, story.Status);
        Assert.Equal("internal", story.Reason);
    }

    [Fact]
    public async Task Import_creates_then_rerun_updates_without_duplicates()
    {
        var rig = new Rig();
        ImportSettings s = Settings();

        ImportResult first = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code, Skills), s, 1, null, CancellationToken.None);
        int distinctImages = 3;
        Assert.All(first.Stories, r => Assert.Equal(Outcome.Imported, r.Outcome));
        Assert.Equal(distinctImages, rig.Store.Saves);

        ImportResult second = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code, Skills), s, 1, null, CancellationToken.None);

        Assert.All(second.Stories, r => Assert.Equal(Outcome.Updated, r.Outcome));
        Assert.Equal(2, rig.Writer.Creates);
        Assert.Equal(2, rig.Writer.Nodes.Count);
        Assert.Equal(distinctImages, rig.Store.Saves);
        Assert.Equal(first.Stories.Select(x => x.ContentKey), second.Stories.Select(x => x.ContentKey));
    }

    [Fact]
    public async Task Edited_post_skipped_with_reason_edited_since_import()
    {
        var rig = new Rig();
        ImportSettings s = Settings();
        ImportResult first = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), s, 1, null, CancellationToken.None);
        rig.Writer.Nodes[first.Stories[0].ContentKey!.Value]["body"] = "<p>edited by hand</p>";

        ImportResult second = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), s, 1, null, CancellationToken.None);

        StoryResult r = second.Stories.Single();
        Assert.Equal(Outcome.Skipped, r.Outcome);
        Assert.Equal("Edited since import", r.Reason);
        Assert.Equal("<p>edited by hand</p>", rig.Writer.Nodes[r.ContentKey!.Value]["body"]);
        PreviewResult preview = await rig.Engine().PreviewAsync(AllFixtures(), s, CancellationToken.None);
        Assert.Equal(StoryStatus.EditedSinceImport, preview.Stories.Single(x => x.PostId == Code).Status);
    }

    [Fact]
    public async Task Unedited_post_updated_in_place()
    {
        var rig = new Rig();
        ImportSettings s = Settings();
        ImportResult first = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), s, 1, null, CancellationToken.None);
        PreviewResult preview = await rig.Engine().PreviewAsync(AllFixtures(), s, CancellationToken.None);
        Assert.Equal(StoryStatus.AlreadyImported, preview.Stories.Single(x => x.PostId == Code).Status);

        ImportResult second = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), s, 1, null, CancellationToken.None);

        Assert.Equal(Outcome.Updated, second.Stories.Single().Outcome);
        Assert.Equal(first.Stories.Single().ContentKey, second.Stories.Single().ContentKey);
        Assert.Equal(1, rig.Writer.Creates);
    }

    [Fact]
    public async Task Deleted_node_created_again()
    {
        var rig = new Rig();
        ImportSettings s = Settings();
        ImportResult first = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), s, 1, null, CancellationToken.None);
        rig.Writer.Nodes.Remove(first.Stories.Single().ContentKey!.Value);

        PreviewResult preview = await rig.Engine().PreviewAsync(AllFixtures(), s, CancellationToken.None);
        Assert.Equal(StoryStatus.New, preview.Stories.Single(x => x.PostId == Code).Status);
        ImportResult second = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), s, 1, null, CancellationToken.None);

        Assert.Equal(Outcome.Imported, second.Stories.Single().Outcome);
        Assert.NotEqual(first.Stories.Single().ContentKey, second.Stories.Single().ContentKey);
        Assert.Equal(2, rig.Writer.Creates);
    }

    [Fact]
    public async Task Adoption_by_original_url_writes_key_row()
    {
        var rig = new Rig();
        var node = Guid.NewGuid();
        rig.Writer.Nodes[node] = new() { ["title"] = "Hand made", ["orig"] = "https://medium.com/@someone/code-and-gists-0a1b2c3d4e5f" };
        rig.Writer.ByOriginalUrl["https://medium.com/@someone/code-and-gists-0a1b2c3d4e5f"] = node;
        ImportSettings s = Settings();

        PreviewResult preview = await rig.Engine().PreviewAsync(AllFixtures(), s, CancellationToken.None);
        Assert.Equal(StoryStatus.AlreadyImported, preview.Stories.Single(x => x.PostId == Code).Status);
        Assert.Equal(node, preview.Stories.Single(x => x.PostId == Code).ContentKey);

        ImportResult run = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), s, 1, null, CancellationToken.None);

        Assert.Equal(Outcome.Skipped, run.Stories.Single().Outcome);
        Assert.Equal(0, rig.Writer.Creates);
        Assert.Equal(node, rig.Keys.Rows[Code].ContentKey);
        Assert.Equal("Hand made", rig.Writer.Nodes[node]["title"]);
    }

    [Fact]
    public async Task No_adoption_without_original_url_mapping()
    {
        var rig = new Rig();
        var node = Guid.NewGuid();
        rig.Writer.Nodes[node] = new() { ["title"] = "Hand made" };
        rig.Writer.ByOriginalUrl["https://medium.com/@someone/code-and-gists-0a1b2c3d4e5f"] = node;
        ImportSettings s = Settings(mapping: Mapping with { OriginalUrl = null });

        PreviewResult preview = await rig.Engine().PreviewAsync(AllFixtures(), s, CancellationToken.None);

        Assert.Equal(StoryStatus.New, preview.Stories.Single(x => x.PostId == Code).Status);
    }

    [Fact]
    public async Task Gist_failure_keeps_link_and_warns()
    {
        var rig = new Rig();
        rig.Gists.Known.Clear();

        ImportResult run = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), Settings(), 1, null, CancellationToken.None);

        StoryResult r = run.Stories.Single();
        Assert.Equal(Outcome.Imported, r.Outcome);
        Assert.Contains(r.Warnings, w => w.Contains(GistId, StringComparison.Ordinal));
        string body = (string)rig.Writer.Nodes[r.ContentKey!.Value]["body"]!;
        Assert.Contains("gist.github.com", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gists_are_fetched_once_per_run()
    {
        var rig = new Rig();

        await rig.Engine().ImportAsync(AllFixtures(), Ids(Code, Fast), Settings(), 1, null, CancellationToken.None);

        Assert.Single(rig.Gists.Requests);
    }

    [Fact]
    public async Task Image_failure_drops_img_and_lists_failure()
    {
        var rig = new Rig();
        rig.Images.Failing.Add("https://cdn-images-1.medium.com/max/800/1*two.png");

        ImportResult run = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), Settings(), 1, null, CancellationToken.None);

        StoryResult r = run.Stories.Single();
        Assert.Contains(r.Failures, f => f.Contains("1*two.png", StringComparison.Ordinal));
        string body = (string)rig.Writer.Nodes[r.ContentKey!.Value]["body"]!;
        Assert.DoesNotContain("1*two.png", body, StringComparison.Ordinal);
        Assert.Contains("/media/", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_host_image_listed_as_warning()
    {
        var rig = new Rig();

        ImportResult run = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), Settings(), 1, null, CancellationToken.None);

        Assert.Contains(run.Stories.Single().Warnings, w => w.Contains("example.com/pic.png", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Images.Requests, u => u.Host == "example.com");
    }

    [Fact]
    public async Task Canonical_this_site_leaves_field_empty()
    {
        var rig = new Rig();

        ImportResult run = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), Settings(CanonicalMode.ThisSite), 1, null, CancellationToken.None);

        Dictionary<string, object?> node = rig.Writer.Nodes[run.Stories.Single().ContentKey!.Value];
        Assert.Null(node["canonical"]);
        Assert.Equal("https://medium.com/@someone/code-and-gists-0a1b2c3d4e5f", node["orig"]);
    }

    [Fact]
    public async Task Canonical_keep_medium_writes_url()
    {
        var rig = new Rig();

        ImportResult run = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), Settings(CanonicalMode.KeepMedium), 1, null, CancellationToken.None);

        Dictionary<string, object?> node = rig.Writer.Nodes[run.Stories.Single().ContentKey!.Value];
        Assert.Equal("https://medium.com/@someone/code-and-gists-0a1b2c3d4e5f", node["canonical"]);
    }

    [Fact]
    public async Task Slug_policy_value_passed_as_url_segment()
    {
        var rig = new Rig();
        rig.Slugs.Add(new FixedSlug("my-slug"));

        ImportResult run = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code), Settings(), 1, null, CancellationToken.None);

        Assert.Equal("my-slug", rig.Writer.Last[run.Stories.Single().ContentKey!.Value].UrlSegment);
    }

    [Fact]
    public async Task Callback_called_per_story_and_once_at_end()
    {
        var rig = new Rig();

        await rig.Engine().ImportAsync(AllFixtures(), Ids(Code, Skills), Settings(), 1, null, CancellationToken.None);

        Assert.Equal(2, rig.Spy.Stories.Count);
        Assert.Contains(Code, rig.Spy.Stories);
        Assert.Contains(Skills, rig.Spy.Stories);
        Assert.Equal(1, rig.Spy.Completed);
    }

    [Fact]
    public async Task One_story_throwing_does_not_stop_the_run()
    {
        var rig = new Rig();
        rig.Writer.ThrowForName = "Code and gists";

        ImportResult run = await rig.Engine().ImportAsync(AllFixtures(), Ids(Code, Skills), Settings(), 1, null, CancellationToken.None);

        StoryResult bad = run.Stories.Single(x => x.PostId == Code);
        Assert.Equal(Outcome.Failed, bad.Outcome);
        Assert.Contains("writer failed", bad.Reason, StringComparison.Ordinal);
        Assert.Equal(Outcome.Imported, run.Stories.Single(x => x.PostId == Skills).Outcome);
        Assert.False(rig.Keys.Rows.ContainsKey(Code));
    }

    [Fact]
    public async Task Cancel_after_first_post_then_rerun_shows_it_already_imported()
    {
        var rig = new Rig();
        using var cts = new CancellationTokenSource();
        rig.Spy.AfterStory = _ => cts.Cancel();
        ImportSettings s = Settings();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rig.Engine().ImportAsync(AllFixtures(), Ids(Code, Skills), s, 1, null, cts.Token));

        Assert.Single(rig.Keys.Rows);
        PreviewResult preview = await rig.Engine().PreviewAsync(AllFixtures(), s, CancellationToken.None);
        Assert.Equal(StoryStatus.AlreadyImported, preview.Stories.Single(x => x.PostId == Code).Status);
        Assert.Equal(StoryStatus.New, preview.Stories.Single(x => x.PostId == Skills).Status);
    }

    [Fact]
    public async Task Duplicate_post_ids_first_entry_wins()
    {
        var rig = new Rig();
        (string name, string html) = Post("2024-01-02");
        using MemoryStream zip = Zip((name, html), ("posts/2024-01-03_Copy-0a1b2c3d4e5f.html", html.Replace("Code and gists", "Second copy")));

        PreviewResult preview = await rig.Engine().PreviewAsync(zip, Settings(), CancellationToken.None);

        PreviewStory only = Assert.Single(preview.Stories);
        Assert.Equal("Code and gists", only.Title);
    }

    [Fact]
    public async Task Blank_title_falls_back_to_post_id_for_name_and_folder()
    {
        var rig = new Rig();
        (string name, string html) = Post("2024-01-02");
        string blank = html.Replace("Code and gists", " ");
        using MemoryStream zip = Zip((name, blank));

        ImportResult run = await rig.Engine().ImportAsync(zip, Ids(Code), Settings(), 1, null, CancellationToken.None);

        StoryResult r = run.Stories.Single();
        Assert.Equal(Code, r.Title);
        Assert.Equal(Code, rig.Writer.Last[r.ContentKey!.Value].Name);
        Assert.All(rig.Store.Folders, f => Assert.EndsWith(" " + Code, f, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Non_seekable_zip_is_buffered_and_read()
    {
        var rig = new Rig();
        using var zip = new NonSeekable(AllFixtures());

        PreviewResult preview = await rig.Engine().PreviewAsync(zip, Settings(), CancellationToken.None);

        Assert.Equal(5, preview.Stories.Count);
    }

    [Fact]
    public async Task Non_seekable_zip_over_the_upload_cap_throws()
    {
        var rig = new Rig { };
        rig.Options.MaxUploadBytes = 100;
        using var zip = new NonSeekable(AllFixtures());

        await Assert.ThrowsAsync<MediumExportException>(() => rig.Engine().PreviewAsync(zip, Settings(), CancellationToken.None));
    }

    [Fact]
    public async Task Seekable_zip_over_the_upload_cap_is_used_as_is()
    {
        var rig = new Rig();
        rig.Options.MaxUploadBytes = 100;
        using MemoryStream zip = AllFixtures();

        PreviewResult preview = await rig.Engine().PreviewAsync(zip, Settings(), CancellationToken.None);

        Assert.Equal(5, preview.Stories.Count);
    }
}
