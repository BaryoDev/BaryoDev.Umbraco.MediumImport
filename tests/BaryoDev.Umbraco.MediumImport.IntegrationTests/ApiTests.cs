using System.IO.Compression;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BaryoDev.Umbraco.MediumImport.Api;
using BaryoDev.Umbraco.MediumImport.Engine;
using BaryoDev.Umbraco.MediumImport.Identity;
using BaryoDev.Umbraco.MediumImport.Jobs;
using BaryoDev.Umbraco.MediumImport.Uploads;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.ManagementApi;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public abstract class ApiTestBase : ManagementApiTest<MediumImportController>
{
    protected const string Base = "/umbraco/management/api/v1/medium-import/";
    protected const string PostId = "0a1b2c3d4e5f";
    protected const string RawSecret = "secret-connection-string-xyz";

    protected override Expression<Func<MediumImportController, object>> MethodSelector { get; set; } = c => c.Options(Guid.Empty);

    protected static readonly FieldMapping Mapping =
        new("title", "body", null, null, null, null, null, null);

    protected Guid BlogKey { get; private set; }

    protected override void CustomMvcSetup(IMvcBuilder mvcBuilder) =>
        mvcBuilder.AddApplicationPart(typeof(MediumImportController).Assembly);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<MediumImportOptions>(o => o.MaxUploadBytes = 100_000);
    }

    [SetUp]
    public void SeedTypes() => BlogKey = TypeSeeder.Seed(Services);

    [SetUp]
    public async Task RunPackageMigration()
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
        using IScope scope = Services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
        var syntax = scope.Database.SqlContext.SqlSyntax;
        return syntax.DoesTableExist(scope.Database, StoryKeyRow.TableName)
            && syntax.DoesTableExist(scope.Database, MediaKeyRow.TableName);
    }

    protected ImportSettings Settings() => new(BlogKey, "post", Mapping);

    protected static byte[] ExportZip()
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

        return ms.ToArray();
    }

    protected async Task<Guid> UploadAsync(byte[] bytes)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "export.zip" } };
        HttpResponseMessage response = await Client.PostAsync(Base + "upload", form);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("uploadId").GetGuid();
    }

    protected async Task<HttpResponseMessage> StartImportAsync(Guid uploadId) =>
        await Client.PostAsJsonAsync(Base + "import", new { uploadId, postIds = new[] { PostId }, settings = Settings() });
}

public class ApiTests : ApiTestBase
{
    [Test]
    public async Task Non_settings_user_gets_403()
    {
        await AuthenticateWithSectionsAsync("contentonly", Constants.Applications.Content);
        HttpResponseMessage response = await Client.GetAsync(Base + "settings");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Settings_user_gets_204_when_nothing_is_saved()
    {
        await AuthenticateWithSectionsAsync("settingsuser", Constants.Applications.Settings);
        HttpResponseMessage response = await Client.GetAsync(Base + "settings");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Upload_over_cap_gets_413()
    {
        await AuthenticateWithSectionsAsync("uploader", Constants.Applications.Settings);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[300_000]), "file", "export.zip" } };
        HttpResponseMessage response = await Client.PostAsync(Base + "upload", form);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
    }

    [Test]
    public async Task Preview_returns_stories()
    {
        await AuthenticateWithSectionsAsync("previewer", Constants.Applications.Settings);
        Guid uploadId = await UploadAsync(ExportZip());
        HttpResponseMessage response = await Client.PostAsJsonAsync(Base + "preview", new { uploadId, settings = Settings() });
        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement stories = doc.RootElement.GetProperty("stories");
        Assert.That(stories.GetArrayLength(), Is.EqualTo(1));
        Assert.That(stories[0].GetProperty("postId").GetString(), Is.EqualTo(PostId));
    }

    [Test]
    public async Task Preview_and_import_reject_bad_settings_with_a_plain_400()
    {
        await AuthenticateWithSectionsAsync("badsettings", Constants.Applications.Settings);
        Guid uploadId = await UploadAsync(ExportZip());
        var cases = new (object Settings, string Contains)[]
        {
            (new { parentKey = Guid.NewGuid(), docTypeAlias = "post", mapping = Mapping }, "parent"),
            (new { parentKey = BlogKey, docTypeAlias = "nope", mapping = Mapping }, "does not exist"),
            (new { parentKey = BlogKey, docTypeAlias = "blog", mapping = Mapping }, "not allowed"),
            (new { parentKey = BlogKey, docTypeAlias = "post", mapping = (FieldMapping?)null }, "mapping"),
        };
        foreach ((object settings, string contains) in cases)
        {
            foreach ((string path, object payload) in new (string, object)[]
            {
                ("preview", new { uploadId, settings }),
                ("import", new { uploadId, postIds = new[] { PostId }, settings }),
            })
            {
                HttpResponseMessage response = await Client.PostAsJsonAsync(Base + path, payload);
                string body = await response.Content.ReadAsStringAsync();
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), path + " " + body);
                Assert.That(body, Does.Contain(contains).IgnoreCase, path + " " + body);
            }
        }

        Assert.That((await Client.GetAsync(Base + "settings")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "bad settings were saved");
    }

    [Test]
    public async Task Import_then_poll_until_completed()
    {
        await AuthenticateWithSectionsAsync("importer", Constants.Applications.Settings);
        Guid uploadId = await UploadAsync(ExportZip());
        HttpResponseMessage started = await StartImportAsync(uploadId);
        string startedBody = await started.Content.ReadAsStringAsync();
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.OK), startedBody);
        Guid jobId = JsonDocument.Parse(startedBody).RootElement.GetProperty("jobId").GetGuid();

        string state = "";
        string last = "";
        for (int i = 0; i < 60 && state is not ("completed" or "failed" or "cancelled"); i++)
        {
            await Task.Delay(500);
            HttpResponseMessage poll = await Client.GetAsync(Base + "jobs/" + jobId);
            Assert.That(poll.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            last = await poll.Content.ReadAsStringAsync();
            state = JsonDocument.Parse(last).RootElement.GetProperty("state").GetString()!.ToLowerInvariant();
        }

        Assert.That(state, Is.EqualTo("completed"), last);
        JsonElement stories = JsonDocument.Parse(last).RootElement.GetProperty("result").GetProperty("stories");
        Assert.That(stories.GetArrayLength(), Is.EqualTo(1), last);
        Assert.That(stories[0].GetProperty("outcome").GetString(), Is.EqualTo("Imported").IgnoreCase, last);
        var content = Services.GetRequiredService<IContentService>();
        Assert.That(content.CountChildren(content.GetById(BlogKey)!.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task Unknown_job_returns_404_stopped()
    {
        await AuthenticateWithSectionsAsync("poller", Constants.Applications.Settings);
        HttpResponseMessage response = await Client.GetAsync(Base + "jobs/" + Guid.NewGuid());
        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(JsonDocument.Parse(body).RootElement.GetProperty("error").GetString(), Is.EqualTo("stopped"));
    }
}

/// <summary>The runner is removed so a started job stays Queued, and jobs can be driven by hand.</summary>
public class QueuedJobApiTests : ApiTestBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        foreach (var d in services.Where(d => d.ImplementationType == typeof(ImportJobRunner)).ToList())
        {
            services.Remove(d);
        }
    }

    [Test]
    public async Task Second_import_gets_409()
    {
        await AuthenticateWithSectionsAsync("twice", Constants.Applications.Settings);
        Guid uploadId = await UploadAsync(ExportZip());
        Assert.That((await StartImportAsync(uploadId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await StartImportAsync(uploadId)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Failed_job_response_hides_the_raw_error()
    {
        await AuthenticateWithSectionsAsync("failer", Constants.Applications.Settings);
        Guid uploadId = await UploadAsync(ExportZip());
        string started = await (await StartImportAsync(uploadId)).Content.ReadAsStringAsync();
        Guid jobId = JsonDocument.Parse(started).RootElement.GetProperty("jobId").GetGuid();

        var store = Services.GetRequiredService<ImportJobStore>();
        ImportJob job = store.Claim()!;
        await store.ExecuteAsync(job, (_, _, _) => throw new InvalidOperationException(RawSecret));

        HttpResponseMessage response = await Client.GetAsync(Base + "jobs/" + jobId);
        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("failed").IgnoreCase);
        Assert.That(body, Does.Not.Contain(RawSecret));
    }

    [Test]
    public async Task Cancelling_a_queued_job_deletes_its_upload()
    {
        await AuthenticateWithSectionsAsync("canceller", Constants.Applications.Settings);
        Guid uploadId = await UploadAsync(ExportZip());
        string started = await (await StartImportAsync(uploadId)).Content.ReadAsStringAsync();
        Guid jobId = JsonDocument.Parse(started).RootElement.GetProperty("jobId").GetGuid();
        var uploads = Services.GetRequiredService<UploadStore>();
        Assert.That(File.Exists(uploads.PathOf(uploadId)), Is.True);

        HttpResponseMessage cancel = await Client.PostAsync(Base + "jobs/" + jobId + "/cancel", null);
        Assert.That(cancel.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(File.Exists(uploads.PathOf(uploadId)), Is.False);
    }
}
