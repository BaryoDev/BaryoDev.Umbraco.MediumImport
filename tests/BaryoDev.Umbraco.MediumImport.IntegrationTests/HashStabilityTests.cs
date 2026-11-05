using System.Globalization;
using System.IO.Compression;
using System.Text;
using BaryoDev.Umbraco.MediumImport.Content;
using BaryoDev.Umbraco.MediumImport.Conversion;
using BaryoDev.Umbraco.MediumImport.Engine;
using BaryoDev.Umbraco.MediumImport.Identity;
using BaryoDev.Umbraco.MediumImport.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Extensions;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

/// <summary>
/// The site runs with repository caches on, and the test host turns them off. With them off, a read inside the
/// import scope goes to the database and the stored hash is right by accident, so these tests turn them on.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class HashStabilityTests : ImportFixtureBase
{
    private const string PostId = "2bd5f4aa63ac";
    private const string Published = "2025-03-31T04:28:52.566Z";

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private sealed class PngImages : IImageFetcher
    {
        public Task<FetchedImage?> FetchAsync(Uri url, CancellationToken ct) =>
            Task.FromResult<FetchedImage?>(new FetchedImage("cover.png", Png));
    }

    private sealed class NoGists : IGistClient
    {
        public Task<Gist?> GetAsync(string gistId, CancellationToken ct) => Task.FromResult<Gist?>(null);
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<IStoryKeyRepository, StoryKeyRepository>();
        builder.Services.AddSingleton<IMediaKeyRepository, MediaKeyRepository>();
        builder.Services.AddUnique(new AppCaches(
            new DeepCloneAppCache(new ObjectCacheAppCache()),
            NoAppCache.Instance,
            new IsolatedCaches(_ => new DeepCloneAppCache(new ObjectCacheAppCache()))));
    }

    [SetUp]
    public async Task CreateTables()
    {
        var runner = Services.GetRequiredService<PackageMigrationRunner>();
        await runner.RunPackageMigrationsIfPendingAsync("BaryoDev.Umbraco.MediumImport");
        if (!TablesExist())
        {
            var executor = Services.GetRequiredService<IMigrationPlanExecutor>();
            await executor.ExecutePlanAsync(new MediumImportMigrationPlan(), string.Empty);
        }
    }

    private bool TablesExist()
    {
        using var scope = ScopeProvider.CreateScope(autoComplete: true);
        var syntax = scope.Database.SqlContext.SqlSyntax;
        return syntax.DoesTableExist(scope.Database, StoryKeyRow.TableName)
            && syntax.DoesTableExist(scope.Database, MediaKeyRow.TableName);
    }

    private MediumImportEngine Engine() =>
        new(
            GetRequiredService<IStoryKeyRepository>(),
            GetRequiredService<IMediaKeyRepository>(),
            new PngImages(),
            new NoGists(),
            GetRequiredService<IPostWriter>(),
            GetRequiredService<IMediaStore>(),
            [],
            [],
            [],
            Options.Create(new MediumImportOptions()),
            null,
            GetRequiredService<ICoreScopeProvider>());

    // Every slot the site maps, with tags either as Umbraco.Tags or as a plain text box.
    private ImportSettings SiteSettings(string tagsAlias) =>
        new(BlogKey, "post", new FieldMapping("title", "body", "publishDate", "summary", "cover", tagsAlias, "canonical", "originalUrl"),
            Publish: true, Canonical: CanonicalMode.KeepMedium);

    private static MemoryStream ExportZip()
    {
        const string html = "<html><body><article class=\"h-entry\"><h1 class=\"p-name\">Bakit Baryo.dev?</h1>"
            + "<section data-field=\"subtitle\" class=\"p-summary\">Where the roads were dusty.</section>"
            + "<section data-field=\"body\" class=\"e-content\"><h3 class=\"graf graf--h3 graf--title\">Bakit Baryo.dev?</h3>"
            + "<figure><img src=\"https://miro.medium.com/v2/resize:fit:700/1*cover.png\" alt=\"cover\"></figure>"
            + "<p>I grew up where the roads were dusty.</p></section>"
            + "<footer><time class=\"dt-published\" datetime=\"" + Published + "\"></time>"
            + "<a class=\"p-canonical\" href=\"https://medium.com/@me/bakit-" + PostId + "\">x</a>"
            + "<a class=\"p-tag\" href=\"https://medium.com/tag/reflections\">Reflections</a>"
            + "<a class=\"p-tag\" href=\"https://medium.com/tag/barrio\">Barrio</a></footer></article></body></html>";
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("posts/2025-03-31_Bakit-" + PostId + ".html").Open(), new UTF8Encoding(false));
            w.Write(html);
        }

        ms.Position = 0;
        return ms;
    }

    // On the site the content cache refresher clears the repository caches once the import scope completes.
    // The test host's server messenger does nothing, so clear them here, as a later request would find them.
    private void ClearCachesLikeTheSite()
    {
        AppCaches.IsolatedCaches.ClearAllCaches();
        AppCaches.RuntimeCache.Clear();
    }

    private async Task<StoryResult> ImportAsync(ImportSettings settings)
    {
        ImportResult result = await Engine().ImportAsync(ExportZip(), [PostId], settings, -1, null, CancellationToken.None);
        ClearCachesLikeTheSite();
        return result.Stories.Single();
    }

    private async Task<PreviewStory> PreviewAsync(ImportSettings settings)
    {
        PreviewResult preview = await Engine().PreviewAsync(ExportZip(), settings, CancellationToken.None);
        return preview.Stories.Single();
    }

    [TestCase("tags")]
    [TestCase("tagsText")]
    public async Task Import_then_preview_shows_already_imported_with_every_slot_mapped(string tagsAlias)
    {
        ImportSettings settings = SiteSettings(tagsAlias);

        StoryResult imported = await ImportAsync(settings);
        Assert.That(imported.Outcome, Is.EqualTo(Outcome.Imported), imported.Reason);
        IContent node = Content.GetById(imported.ContentKey!.Value)!;
        Assert.That(node.GetValue<string>("cover"), Does.Contain("mediaKey"), "the cover must be mapped for this test to mean anything");
        Assert.That(node.GetValue("publishDate"), Is.Not.Null);

        PreviewStory preview = await PreviewAsync(settings);

        Assert.That(preview.Status, Is.EqualTo(StoryStatus.AlreadyImported), preview.Reason);
    }

    [TestCase("tags")]
    [TestCase("tagsText")]
    public async Task Rerun_updates_instead_of_skipping(string tagsAlias)
    {
        ImportSettings settings = SiteSettings(tagsAlias);
        StoryResult first = await ImportAsync(settings);
        Assert.That(first.Outcome, Is.EqualTo(Outcome.Imported), first.Reason);
        string? cover = Content.GetById(first.ContentKey!.Value)!.GetValue<string>("cover");

        StoryResult second = await ImportAsync(settings);

        Assert.That(second.Outcome, Is.EqualTo(Outcome.Updated), second.Reason);
        Assert.That(Content.GetById(second.ContentKey!.Value)!.GetValue<string>("cover"), Is.EqualTo(cover), "a rerun must not change the cover");
        Assert.That((await PreviewAsync(settings)).Status, Is.EqualTo(StoryStatus.AlreadyImported));
    }

    // 0.1.0 stored a hash with the publish date as assigned: milliseconds and the Utc kind.
    private void StoreHashLike010(Guid contentKey, ImportSettings settings)
    {
        var mapped = new Dictionary<string, string?>(GetRequiredService<IPostWriter>().ReadMapped(contentKey, settings)!)
        {
            ["publishDate"] = DateTime.Parse(Published, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).ToString("o", CultureInfo.InvariantCulture),
        };
        var stories = GetRequiredService<IStoryKeyRepository>();
        StoryKeyRow row = stories.Get(PostId)!;
        row.ValuesHash = ValuesHash.Compute(mapped);
        stories.Upsert(row);
    }

    [Test]
    public async Task A_hash_stored_by_010_reads_as_already_imported_and_the_next_import_updates()
    {
        ImportSettings settings = SiteSettings("tagsText");
        StoryResult first = await ImportAsync(settings);
        StoreHashLike010(first.ContentKey!.Value, settings);

        Assert.That((await PreviewAsync(settings)).Status, Is.EqualTo(StoryStatus.AlreadyImported));
        Assert.That((await ImportAsync(settings)).Outcome, Is.EqualTo(Outcome.Updated));
        Assert.That(GetRequiredService<IStoryKeyRepository>().Get(PostId)!.ValuesHash,
            Is.EqualTo(ValuesHash.Compute(GetRequiredService<IPostWriter>().ReadMapped(first.ContentKey!.Value, settings)!)));
    }

    [TestCase("publishDate")]
    [TestCase("summary")]
    public async Task A_hand_edit_after_an_010_import_still_reads_as_edited(string alias)
    {
        ImportSettings settings = SiteSettings("tagsText");
        StoryResult first = await ImportAsync(settings);
        StoreHashLike010(first.ContentKey!.Value, settings);

        IContent node = Content.GetById(first.ContentKey!.Value)!;
        node.SetValue(alias, alias == "publishDate" ? new DateTime(2025, 4, 1, 8, 0, 0) : "Edited by hand.");
        Content.Save(node);
        ClearCachesLikeTheSite();

        Assert.That((await PreviewAsync(settings)).Status, Is.EqualTo(StoryStatus.EditedSinceImport));
    }
}
