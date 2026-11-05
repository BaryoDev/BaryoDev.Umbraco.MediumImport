using BaryoDev.Umbraco.MediumImport.Content;
using BaryoDev.Umbraco.MediumImport.Net;
using NUnit.Framework;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Tests.Common.Testing;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class MediaStoreTests : ImportFixtureBase
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Test]
    public void Media_goes_to_medium_import_folder_per_post()
    {
        var store = GetRequiredService<IMediaStore>();

        var a1 = store.Save("post-a", new FetchedImage("one.png", Png), -1);
        var a2 = store.Save("post-a", new FetchedImage("two.png", Png), -1);
        var b1 = store.Save("post-b", new FetchedImage("three.png", Png), -1);

        IMedia[] roots = Media.GetRootMedia().Where(m => m.Name == "Medium import").ToArray();
        Assert.That(roots, Has.Length.EqualTo(1));
        IMedia[] folders = Media.GetPagedChildren(roots[0].Id, 0, 50, out _).ToArray();
        Assert.That(folders.Select(f => f.Name), Is.EquivalentTo(new[] { "post-a", "post-b" }));
        IMedia folderA = folders.Single(f => f.Name == "post-a");
        Assert.That(Media.GetById(a1.MediaKey)!.ParentId, Is.EqualTo(folderA.Id));
        Assert.That(Media.GetById(a2.MediaKey)!.ParentId, Is.EqualTo(folderA.Id));
        Assert.That(Media.GetById(b1.MediaKey)!.ParentId, Is.EqualTo(folders.Single(f => f.Name == "post-b").Id));
        Assert.That(Media.GetById(a1.MediaKey)!.ContentType.Alias, Is.EqualTo("Image"));
        Assert.That(a1.Url, Does.EndWith("one.png"));
        Assert.That(store.Find(a1.MediaKey)!.Url, Is.EqualTo(a1.Url));
        Assert.That(store.Find(Guid.NewGuid()), Is.Null);
    }
}
