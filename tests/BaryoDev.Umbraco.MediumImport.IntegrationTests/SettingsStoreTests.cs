using BaryoDev.Umbraco.MediumImport.Engine;
using BaryoDev.Umbraco.MediumImport.Settings;
using BaryoDev.Umbraco.MediumImport.Uploads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class SettingsStoreTests : UmbracoIntegrationTest
{
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        builder.Services.TryAddSingleton<SettingsStore>();
        builder.Services.TryAddSingleton<UploadStore>();
        builder.Services.AddOptions<MediumImportOptions>();
    }

    [Test]
    public void Settings_roundtrip()
    {
        var store = GetRequiredService<SettingsStore>();
        Assert.That(store.Load(), Is.Null);
        var saved = new ImportSettings(
            Guid.NewGuid(), "post", new FieldMapping("title", "body", "date", null, "cover", "tags", null, "orig"),
            Publish: true, Canonical: CanonicalMode.KeepMedium);

        store.Save(saved);
        ImportSettings? loaded = store.Load();

        Assert.That(loaded, Is.EqualTo(saved));
        Assert.That(GetRequiredService<IKeyValueService>().GetValue(SettingsStore.Key), Does.Contain("KeepMedium"));
    }

    [Test]
    public void Settings_load_ignores_garbage()
    {
        GetRequiredService<IKeyValueService>().SetValue(SettingsStore.Key, "{not json");

        Assert.That(GetRequiredService<SettingsStore>().Load(), Is.Null);
    }

    [Test]
    public void Upload_root_is_under_local_temp_not_wwwroot()
    {
        string root = GetRequiredService<UploadStore>().Root;
        var hosting = GetRequiredService<IHostingEnvironment>();

        Assert.That(root, Does.StartWith(hosting.LocalTempPath));
        Assert.That(root.Split(Path.DirectorySeparatorChar), Has.None.EqualTo("wwwroot"));
    }
}
