using BaryoDev.Umbraco.MediumImport.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class MigrationTests : UmbracoIntegrationTest
{
    private IStoryKeyRepository Stories => GetRequiredService<IStoryKeyRepository>();

    private IMediaKeyRepository Media => GetRequiredService<IMediaKeyRepository>();

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        builder.Services.TryAddSingleton<IStoryKeyRepository, StoryKeyRepository>();
        builder.Services.TryAddSingleton<IMediaKeyRepository, MediaKeyRepository>();
    }

    [SetUp]
    public async Task RunPackageMigration()
    {
        // Idempotent: a no-op when the plan already ran at boot.
        var runner = Services.GetRequiredService<PackageMigrationRunner>();
        await runner.RunPackageMigrationsIfPendingAsync("BaryoDev.Umbraco.MediumImport");
    }

    [Test]
    public void Tables_exist_and_roundtrip()
    {
        using (var scope = ScopeProvider.CreateScope(autoComplete: true))
        {
            Assert.That(scope.Database.SqlContext.SqlSyntax.DoesTableExist(scope.Database, StoryKeyRow.TableName), Is.True);
            Assert.That(scope.Database.SqlContext.SqlSyntax.DoesTableExist(scope.Database, MediaKeyRow.TableName), Is.True);
        }

        var key = Guid.NewGuid();
        var at = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Stories.Upsert(new StoryKeyRow { PostId = "abc123", ContentKey = key, ValuesHash = "h1", ImportedUtc = at });

        var row = Stories.Get("abc123");
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.ContentKey, Is.EqualTo(key));
        Assert.That(row.ValuesHash, Is.EqualTo("h1"));
        Assert.That(Stories.Get("missing"), Is.Null);

        Stories.Upsert(new StoryKeyRow { PostId = "abc123", ContentKey = key, ValuesHash = "h2", ImportedUtc = at });
        Assert.That(Stories.Get("abc123")!.ValuesHash, Is.EqualTo("h2"));
        using (var scope = ScopeProvider.CreateScope(autoComplete: true))
        {
            Assert.That(scope.Database.ExecuteScalar<int>("SELECT COUNT(*) FROM " + StoryKeyRow.TableName), Is.EqualTo(1));
        }

        var url = new Uri("https://cdn-images-1.medium.com/max/800/a.png");
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        Assert.That(Media.Get(url), Is.Null);
        Media.Upsert(url, m1);
        Assert.That(Media.Get(url), Is.EqualTo(m1));
        Media.Upsert(url, m2);
        Assert.That(Media.Get(url), Is.EqualTo(m2));
        using (var scope = ScopeProvider.CreateScope(autoComplete: true))
        {
            Assert.That(scope.Database.ExecuteScalar<int>("SELECT COUNT(*) FROM " + MediaKeyRow.TableName), Is.EqualTo(1));
        }
    }

    [Test]
    public void Story_upsert_after_racing_insert_updates()
    {
        var repo = (StoryKeyRepository)Stories;
        var key = Guid.NewGuid();
        var at = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        repo.Upsert(new StoryKeyRow { PostId = "race1", ContentKey = Guid.NewGuid(), ValuesHash = "first", ImportedUtc = at });

        repo.UpsertCore(new StoryKeyRow { PostId = "race1", ContentKey = key, ValuesHash = "second", ImportedUtc = at }, skipRead: true);

        var row = repo.Get("race1");
        Assert.That(row!.ValuesHash, Is.EqualTo("second"));
        Assert.That(row.ContentKey, Is.EqualTo(key));
    }

    [Test]
    public void Media_upsert_after_racing_insert_updates()
    {
        var repo = (MediaKeyRepository)Media;
        var url = new Uri("https://cdn-images-1.medium.com/max/800/race.png");
        var second = Guid.NewGuid();
        repo.Upsert(url, Guid.NewGuid());

        repo.UpsertCore(url, second, skipRead: true);

        Assert.That(repo.Get(url), Is.EqualTo(second));
    }

    [Test]
    public void Upsert_with_missing_table_throws()
    {
        using (var scope = ScopeProvider.CreateScope(autoComplete: true))
        {
            scope.Database.Execute("DROP TABLE " + StoryKeyRow.TableName);
        }

        Assert.That(
            () => Stories.Upsert(new StoryKeyRow { PostId = "x", ContentKey = Guid.NewGuid(), ValuesHash = "h", ImportedUtc = DateTime.UtcNow }),
            Throws.Exception);
    }
}
