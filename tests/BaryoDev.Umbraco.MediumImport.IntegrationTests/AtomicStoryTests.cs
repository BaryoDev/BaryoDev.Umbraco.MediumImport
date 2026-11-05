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
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Tests.Common.Testing;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

public sealed class CancelPublishHandler : INotificationHandler<ContentPublishingNotification>
{
    public static volatile bool Enabled;

    public void Handle(ContentPublishingNotification notification)
    {
        if (Enabled)
        {
            notification.CancelOperation(new EventMessage("test", "publish blocked", EventMessageType.Error));
        }
    }
}

[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class AtomicStoryTests : ImportFixtureBase
{
    private const string PostId = "0a1b2c3d4e5f";

    private static readonly FieldMapping Plain = new("title", "body", null, null, null, null, null, null);

    private sealed class NoImages : IImageFetcher
    {
        public Task<FetchedImage?> FetchAsync(Uri url, CancellationToken ct) => Task.FromResult<FetchedImage?>(null);
    }

    private sealed class NoGists : IGistClient
    {
        public Task<Gist?> GetAsync(string gistId, CancellationToken ct) => Task.FromResult<Gist?>(null);
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.AddNotificationHandler<ContentPublishingNotification, CancelPublishHandler>();
        builder.Services.AddSingleton<IStoryKeyRepository, StoryKeyRepository>();
        builder.Services.AddSingleton<IMediaKeyRepository, MediaKeyRepository>();
    }

    [SetUp]
    public async Task CreateTables()
    {
        CancelPublishHandler.Enabled = false;
        var runner = Services.GetRequiredService<PackageMigrationRunner>();
        await runner.RunPackageMigrationsIfPendingAsync("BaryoDev.Umbraco.MediumImport");
        if (!TablesExist())
        {
            var executor = Services.GetRequiredService<IMigrationPlanExecutor>();
            await executor.ExecutePlanAsync(new MediumImportMigrationPlan(), string.Empty);
        }
    }

    [TearDown]
    public void ResetHandler() => CancelPublishHandler.Enabled = false;

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
            new NoImages(),
            new NoGists(),
            GetRequiredService<IPostWriter>(),
            GetRequiredService<IMediaStore>(),
            [],
            [],
            [],
            Options.Create(new MediumImportOptions()),
            null,
            GetRequiredService<ICoreScopeProvider>());

    private static MemoryStream ExportZip()
    {
        const string html = "<html><body><article class=\"h-entry\"><h1 class=\"p-name\">Hello</h1>"
            + "<section data-field=\"body\" class=\"e-content\"><h3 class=\"graf graf--h3 graf--title\">Hello</h3><p>Plain text.</p></section>"
            + "<footer><time class=\"dt-published\" datetime=\"2024-01-02T10:00:00.000Z\"></time>"
            + "<a class=\"p-canonical\" href=\"https://medium.com/@me/hello-" + PostId + "\">x</a></footer></article></body></html>";
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("posts/2024-01-02_Hello-" + PostId + ".html").Open(), new UTF8Encoding(false));
            w.Write(html);
        }

        ms.Position = 0;
        return ms;
    }

    private int ChildCount() => Content.Count("post");

    [Test]
    public async Task Failed_publish_leaves_no_node_and_no_key_row_and_a_rerun_creates_one_node()
    {
        ImportSettings settings = new(BlogKey, "post", Plain, Publish: true);

        CancelPublishHandler.Enabled = true;
        ImportResult first = await Engine().ImportAsync(ExportZip(), [PostId], settings, -1, null, CancellationToken.None);

        Assert.That(first.Stories.Single().Outcome, Is.EqualTo(Outcome.Failed));
        Assert.That(ChildCount(), Is.EqualTo(0), "the saved node must be rolled back");
        Assert.That(GetRequiredService<IStoryKeyRepository>().Get(PostId), Is.Null);

        CancelPublishHandler.Enabled = false;
        ImportResult second = await Engine().ImportAsync(ExportZip(), [PostId], settings, -1, null, CancellationToken.None);

        Assert.That(second.Stories.Single().Outcome, Is.EqualTo(Outcome.Imported));
        Assert.That(ChildCount(), Is.EqualTo(1));
        Assert.That(GetRequiredService<IStoryKeyRepository>().Get(PostId), Is.Not.Null);
    }
}
