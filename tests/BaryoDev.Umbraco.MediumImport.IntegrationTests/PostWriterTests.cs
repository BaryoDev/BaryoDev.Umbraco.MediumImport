using System.Text.Json;
using BaryoDev.Umbraco.MediumImport.Content;
using BaryoDev.Umbraco.MediumImport.Engine;
using NUnit.Framework;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Tests.Common.Testing;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class PostWriterTests : ImportFixtureBase
{
    private const int User = -1;

    private IPostWriter Writer => GetRequiredService<IPostWriter>();

    private static PostValues Values(string name = "My post", string? slug = "my-post", Guid? cover = null, string[]? tags = null) =>
        new(
            name,
            slug,
            new Dictionary<string, object?>
            {
                ["title"] = name,
                ["body"] = "<p>hello</p>",
                ["publishDate"] = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                ["summary"] = "short",
                ["tags"] = tags ?? new[] { "a", "b" },
                ["canonical"] = "https://medium.com/p/1",
                ["originalUrl"] = "https://medium.com/p/1",
            },
            cover);

    [Test]
    public void Create_sets_all_mapped_fields_and_name()
    {
        Guid key = Writer.Write(Settings(), Values(), null, User);

        IContent node = Content.GetById(key)!;
        Assert.That(node.Name, Is.EqualTo("My post"));
        Assert.That(node.GetValue<string>("title"), Is.EqualTo("My post"));
        Assert.That(node.GetValue<string>("body"), Is.EqualTo("<p>hello</p>"));
        Assert.That(node.GetValue<DateTime>("publishDate"), Is.EqualTo(new DateTime(2026, 3, 4, 5, 6, 7)));
        Assert.That(node.GetValue<string>("summary"), Is.EqualTo("short"));
        Assert.That(node.GetValue<string>("canonical"), Is.EqualTo("https://medium.com/p/1"));
        Assert.That(node.GetValue<string>("originalUrl"), Is.EqualTo("https://medium.com/p/1"));
        Assert.That(node.GetValue<string>("umbracoUrlName"), Is.EqualTo("my-post"));
        Assert.That(node.Published, Is.False);
        Assert.That(Writer.FindByOriginalUrl(Settings(), "https://medium.com/p/1"), Is.EqualTo(key));
        Assert.That(Writer.FindByOriginalUrl(Settings(), "https://medium.com/p/2"), Is.Null);
    }

    [Test]
    public void Update_keeps_name_slug_and_unpublished_state()
    {
        Guid key = Writer.Write(Settings(), Values(), null, User);

        Writer.Write(Settings(), Values("Renamed", "other-slug"), key, User);

        IContent node = Content.GetById(key)!;
        Assert.That(node.Name, Is.EqualTo("My post"));
        Assert.That(node.GetValue<string>("umbracoUrlName"), Is.EqualTo("my-post"));
        Assert.That(node.GetValue<string>("title"), Is.EqualTo("Renamed"));
        Assert.That(node.Published, Is.False);
    }

    [Test]
    public void Publish_true_publishes_new_node()
    {
        Guid key = Writer.Write(Settings(publish: true), Values(), null, User);
        Assert.That(Content.GetById(key)!.Published, Is.True);

        Writer.Write(Settings(publish: false), Values("Again"), key, User);
        IContent node = Content.GetById(key)!;
        Assert.That(node.Published, Is.True);
        Assert.That(node.Name, Is.EqualTo("My post"));
    }

    [Test]
    public void Tags_editor_gets_items()
    {
        Guid key = Writer.Write(Settings(), Values(tags: ["alpha", "beta"]), null, User);

        string raw = Content.GetById(key)!.GetValue<string>("tags")!;
        Assert.That(JsonSerializer.Deserialize<string[]>(raw)!, Is.EquivalentTo(new[] { "alpha", "beta" }));
    }

    [Test]
    public void Tags_in_a_text_editor_are_comma_joined()
    {
        var mapping = FullMapping with { Tags = "summary", Summary = null };
        var values = new PostValues(
            "Text tags", null,
            new Dictionary<string, object?> { ["title"] = "Text tags", ["body"] = "<p>x</p>", ["summary"] = new[] { "x", "y" } },
            null);

        Guid key = Writer.Write(Settings(mapping: mapping), values, null, User);

        Assert.That(Content.GetById(key)!.GetValue<string>("summary"), Is.EqualTo("x,y"));
    }

    [Test]
    public void Slot_maps_to_a_property_with_a_different_alias()
    {
        var mapping = FullMapping with { Title = "headline" };
        var values = new PostValues(
            "Aliased", null,
            new Dictionary<string, object?> { ["headline"] = "Aliased headline", ["body"] = "<p>x</p>" },
            null);

        Guid key = Writer.Write(Settings(mapping: mapping), values, null, User);

        IContent node = Content.GetById(key)!;
        Assert.That(node.GetValue<string>("headline"), Is.EqualTo("Aliased headline"));
        Assert.That(node.GetValue<string>("title"), Is.Null);
        Assert.That(Writer.ReadMapped(key, Settings(mapping: mapping))!["headline"], Is.EqualTo("Aliased headline"));
    }

    [Test]
    public void Update_keeps_unpublished_node_unpublished_with_publish_true()
    {
        Guid key = Writer.Write(Settings(publish: false), Values(), null, User);

        Writer.Write(Settings(publish: true), Values("Again"), key, User);

        Assert.That(Content.GetById(key)!.Published, Is.False);
    }

    [Test]
    public void Cover_points_at_media()
    {
        IMedia item = Media.CreateMedia("cover", -1, "Image");
        Media.Save(item);

        Guid key = Writer.Write(Settings(), Values(cover: item.Key), null, User);

        string json = Content.GetById(key)!.GetValue<string>("cover")!;
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement[0].GetProperty("mediaKey").GetGuid(), Is.EqualTo(item.Key));
    }

    [Test]
    public void Mapping_options_filter_by_editor()
    {
        var options = GetRequiredService<MappingOptions>().For(BlogKey);

        Assert.That(options.Select(o => o.Alias), Is.EquivalentTo(new[] { "post", "bare" }));
        var post = options.Single(o => o.Alias == "post").Slots;
        Assert.That(post["body"].Select(p => p.Alias), Is.EqualTo(new[] { "body" }));
        Assert.That(post["publishDate"].Select(p => p.Alias), Is.EqualTo(new[] { "publishDate" }));
        Assert.That(post["cover"].Select(p => p.Alias), Is.EqualTo(new[] { "cover" }));
        Assert.That(post["title"].Select(p => p.Alias), Does.Contain("summary").And.Not.Contain("body").And.Not.Contain("tags"));
        Assert.That(post["tags"].Select(p => p.Alias), Does.Contain("tags").And.Contain("title").And.Not.Contain("cover"));
        var bare = options.Single(o => o.Alias == "bare").Slots;
        Assert.That(bare["cover"], Is.Empty);
        Assert.That(bare["body"].Select(p => p.Alias), Is.EqualTo(new[] { "body" }));
        Assert.That(GetRequiredService<MappingOptions>().For(Guid.NewGuid()), Is.Empty);
    }

    [Test]
    public void Slug_hook_ignored_when_doc_type_has_no_umbracoUrlName()
    {
        var mapping = new FieldMapping("title", "body", null, null, null, null, null, null);
        var values = new PostValues(
            "Bare post", "wanted-slug",
            new Dictionary<string, object?> { ["title"] = "Bare post", ["body"] = "<p>x</p>" }, null);

        Guid key = Writer.Write(Settings("bare", mapping: mapping), values, null, User);

        IContent node = Content.GetById(key)!;
        Assert.That(node.HasProperty("umbracoUrlName"), Is.False);
        Assert.That(node.Name, Is.EqualTo("Bare post"));
    }

    [Test]
    public void ReadMapped_returns_values_for_an_imported_node()
    {
        Guid key = Writer.Write(Settings(), Values(), null, User);

        var mapped = Writer.ReadMapped(key, Settings())!;

        Assert.That(mapped["title"], Is.EqualTo("My post"));
        Assert.That(mapped["originalUrl"], Is.EqualTo("https://medium.com/p/1"));
    }

    [Test]
    public void ReadMapped_is_null_when_the_node_is_gone()
    {
        Guid key = Writer.Write(Settings(), Values(), null, User);
        Content.Delete(Content.GetById(key)!);

        Assert.That(Writer.ReadMapped(key, Settings()), Is.Null);
        Assert.That(Writer.ReadMapped(Guid.NewGuid(), Settings()), Is.Null);
    }

    [Test]
    public void ReadMapped_is_null_when_the_node_is_not_under_the_parent()
    {
        Guid key = Writer.Write(Settings(), Values(), null, User);
        IContent other = Content.Create("Other blog", -1, "blog");
        Content.Save(other);

        Assert.That(Writer.ReadMapped(key, Settings() with { ParentKey = other.Key }), Is.Null);
    }

    [Test]
    public void ReadMapped_is_null_when_the_node_is_another_doc_type()
    {
        Guid key = Writer.Write(Settings(), Values(), null, User);

        Assert.That(Writer.ReadMapped(key, Settings("bare")), Is.Null);
    }
}
