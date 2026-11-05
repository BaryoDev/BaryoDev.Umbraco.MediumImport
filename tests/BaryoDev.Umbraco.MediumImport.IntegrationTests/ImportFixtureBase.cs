using BaryoDev.Umbraco.MediumImport.Content;
using BaryoDev.Umbraco.MediumImport.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public abstract class ImportFixtureBase : UmbracoIntegrationTest
{
    protected static readonly FieldMapping FullMapping =
        new("title", "body", "publishDate", "summary", "cover", "tags", "canonical", "originalUrl");

    protected IContentService Content => GetRequiredService<IContentService>();

    protected IMediaService Media => GetRequiredService<IMediaService>();

    protected Guid BlogKey { get; private set; }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        builder.Services.TryAddTransient<IPostWriter, UmbracoPostWriter>();
        builder.Services.TryAddTransient<IMediaStore, UmbracoMediaStore>();
        builder.Services.TryAddTransient<MappingOptions>();
    }

    [SetUp]
    public void SeedTypes()
    {
        using (var scope = ScopeProvider.CreateScope(autoComplete: true))
        {
            var textBox = DataType("Umbraco.TextBox");
            var textArea = DataType("Umbraco.TextArea");
            var richText = DataType("Umbraco.RichText");
            var date = DataType("Umbraco.DateTime");
            var picker = DataType("Umbraco.MediaPicker3");
            var tags = DataType("Umbraco.Tags");

            CreateType("post", false, [], new (string, IDataType)[]
            {
                ("title", textBox), ("body", richText), ("publishDate", date), ("summary", textArea),
                ("cover", picker), ("tags", tags), ("canonical", textBox), ("originalUrl", textBox),
                ("headline", textBox), ("umbracoUrlName", textBox),
            });
            CreateType("bare", false, [], new (string, IDataType)[] { ("title", textBox), ("body", richText) });
            CreateType("blog", true, ["post", "bare"], new (string, IDataType)[] { ("title", textBox) });

            IContent blog = Content.Create("Blog", Constants.System.Root, "blog");
            Content.Save(blog);
            var published = Content.Publish(blog, ["*"]);
            Assert.That(published.Success, Is.True, "publish blog: " + published.Result);
            BlogKey = blog.Key;
        }
    }

    protected ImportSettings Settings(string docType = "post", bool publish = false, FieldMapping? mapping = null) =>
        new(BlogKey, docType, mapping ?? FullMapping, publish);

    private IDataType DataType(string editorAlias)
    {
        var editors = GetRequiredService<PropertyEditorCollection>();
        Assert.That(editors.TryGet(editorAlias, out IDataEditor? editor), Is.True, editorAlias);
        var dt = new DataType(editor!, GetRequiredService<IConfigurationEditorJsonSerializer>()) { Name = "mi-" + editorAlias };
        var result = GetRequiredService<IDataTypeService>().CreateAsync(dt, Constants.Security.SuperUserKey).GetAwaiter().GetResult();
        Assert.That(result.Success, Is.True, editorAlias + " " + result.Status);
        return result.Result;
    }

    private void CreateType(string alias, bool root, string[] allowed, (string Alias, IDataType Type)[] props)
    {
        var shortString = GetRequiredService<IShortStringHelper>();
        var types = GetRequiredService<IContentTypeService>();
        var ct = new ContentType(shortString, Constants.System.Root) { Alias = alias, Name = alias, AllowedAsRoot = root };
        foreach (var (propAlias, dataType) in props)
        {
            ct.AddPropertyType(new PropertyType(shortString, dataType) { Alias = propAlias, Name = propAlias }, "Content", "Content");
        }

        int sort = 0;
        ct.AllowedContentTypes = allowed
            .Select(a => types.Get(a)!)
            .Select(t => new ContentTypeSort(t.Key, sort++, t.Alias))
            .ToList();
        var result = types.CreateAsync(ct, Constants.Security.SuperUserKey).GetAwaiter().GetResult();
        Assert.That(result.Success, Is.True, alias + " " + result.Result);
    }
}
