using System.Text.Json.Nodes;
using BaryoDev.Umbraco.MediumImport.Content;
using BaryoDev.Umbraco.MediumImport.Conversion;
using BaryoDev.Umbraco.MediumImport.Identity;
using NUnit.Framework;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

/// <summary>Saves an imported body through the backoffice path (IContentEditingService) and reads it back.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class RichTextRoundTripTests : ImportFixtureBase
{
    private const int User = -1;
    private const string Block = "func main() {\n    run()\n    \n}";
    private static readonly Guid MediaKey = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private IPostWriter Writer => GetRequiredService<IPostWriter>();

    private IContentEditingService Editing => GetRequiredService<IContentEditingService>();

    [Test]
    public async Task Language_class_and_newlines_survive_rte_save()
    {
        Guid key = WriteFixtureBody();

        await ResaveUnchanged(key);

        string stored = StoredMarkup(key);
        Assert.That(stored, Does.Contain("<code class=\"language-go\">"), stored);
        Assert.That(stored, Does.Contain(Block), stored);
    }

    [Test]
    public async Task Media_udi_survives_rte_save()
    {
        Guid key = WriteFixtureBody();
        string expected = "data-udi=\"umb://media/" + MediaKey.ToString("N") + "\"";
        Assert.That(StoredMarkup(key), Does.Contain(expected), "before save");

        await ResaveUnchanged(key);

        string stored = StoredMarkup(key);
        Assert.That(stored, Does.Contain(expected), stored);
    }

    [Test]
    public async Task Save_without_change_keeps_hash()
    {
        Guid key = WriteFixtureBody();
        string before = ValuesHash.Compute(Writer.ReadMapped(key, Settings())!);

        await ResaveUnchanged(key);

        string after = ValuesHash.Compute(Writer.ReadMapped(key, Settings())!);
        Assert.That(after, Is.EqualTo(before));
    }

    private Guid WriteFixtureBody()
    {
        string html = System.IO.File.ReadAllText(FixturePath());
        const string open = "<section data-field=\"body\" class=\"e-content\">";
        int start = html.IndexOf(open, StringComparison.Ordinal) + open.Length;
        string section = html[start..html.IndexOf("</section>", start, StringComparison.Ordinal)];

        var context = new SanitizeContext(
            new Dictionary<Uri, ResolvedImage>
            {
                [new Uri("https://cdn-images-1.medium.com/max/800/1*one.png")] = new("/media/one.png", MediaKey),
            },
            new Dictionary<string, Gist>());
        string body = PostHtmlSanitizer.Sanitize(section, context);

        var values = new PostValues(
            "Code and gists",
            "code-and-gists",
            new Dictionary<string, object?> { ["title"] = "Code and gists", ["body"] = body },
            null);
        return Writer.Write(Settings(), values, null, User);
    }

    private async Task ResaveUnchanged(Guid key)
    {
        IContent node = Content.GetById(key)!;
        var properties = node.Properties
            .Select(p => new PropertyValueModel { Alias = p.Alias, Value = EditorValue(p) })
            .ToList();
        var model = new ContentUpdateModel
        {
            Properties = properties,
            Variants = new[] { new VariantModel { Name = node.Name! } },
        };

        var result = await Editing.UpdateAsync(key, model, Constants.Security.SuperUserKey);
        Assert.That(result.Success, Is.True, "editing save: " + result.Status);
    }

    // The backoffice sends the rich text value as {markup, blocks}; other values go back as stored.
    private static object? EditorValue(IProperty p)
    {
        object? raw = p.GetValue();
        if (p.PropertyType.PropertyEditorAlias != "Umbraco.RichText" || raw is null)
        {
            return raw;
        }

        string text = Convert.ToString(raw)!;
        if (text.TrimStart().StartsWith('{'))
        {
            return JsonNode.Parse(text);
        }

        return new JsonObject { ["markup"] = text, ["blocks"] = null };
    }

    private string StoredMarkup(Guid key)
    {
        string raw = Content.GetById(key)!.GetValue<string>("body")!;
        if (raw.TrimStart().StartsWith('{'))
        {
            return JsonNode.Parse(raw)!["markup"]!.GetValue<string>();
        }

        return raw;
    }

    private static string FixturePath()
    {
        for (DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(
                dir.FullName, "tests", "BaryoDev.Umbraco.MediumImport.Tests", "fixtures", "posts",
                "2024-01-02_Code-and-gists-0a1b2c3d4e5f.html");
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("code fixture not found above " + TestContext.CurrentContext.TestDirectory);
    }
}
