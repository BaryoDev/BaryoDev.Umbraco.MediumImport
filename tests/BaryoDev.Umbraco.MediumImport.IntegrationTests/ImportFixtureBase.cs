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
using Umbraco.Cms.Tests.Integration.Testing;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

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
    public async Task SeedTypes()
    {
        var textBox = await DataType("Umbraco.TextBox");
        var textArea = await DataType("Umbraco.TextArea");
        var richText = await DataType("Umbraco.RichText");
        var date = await DataType("Umbraco.DateTime");
        var picker = await DataType("Umbraco.MediaPicker3");
        var tags = await DataType("Umbraco.Tags");

        await CreateType("post", false, [], new (string, IDataType)[]
        {
            ("title", textBox), ("body", richText), ("publishDate", date), ("summary", textArea),
            ("cover", picker), ("tags", tags), ("canonical", textBox), ("originalUrl", textBox),
            ("umbracoUrlName", textBox),
        });
        await CreateType("bare", false, [], new (string, IDataType)[] { ("title", textBox), ("body", richText) });
        await CreateType("blog", true, ["post", "bare"], new (string, IDataType)[] { ("title", textBox) });

        IContent blog = Content.Create("Blog", Constants.System.Root, "blog");
        Content.Save(blog);
        BlogKey = blog.Key;
    }

    protected ImportSettings Settings(string docType = "post", bool publish = false, FieldMapping? mapping = null) =>
        new(BlogKey, docType, mapping ?? FullMapping, publish);

    private async Task<IDataType> DataType(string editorAlias)
    {
        var editors = GetRequiredService<PropertyEditorCollection>();
        Assert.That(editors.TryGet(editorAlias, out IDataEditor? editor), Is.True, editorAlias);
        var dt = new DataType(editor!, GetRequiredService<IConfigurationEditorJsonSerializer>()) { Name = "mi-" + editorAlias };
        var result = await GetRequiredService<IDataTypeService>().CreateAsync(dt, Constants.Security.SuperUserKey);
        Assert.That(result.Success, Is.True, editorAlias + " " + result.Status);
        return result.Result;
    }

    private async Task CreateType(string alias, bool root, string[] allowed, (string Alias, IDataType Type)[] props)
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
        var result = await types.CreateAsync(ct, Constants.Security.SuperUserKey);
        Assert.That(result.Success, Is.True, alias + " " + result.Result);
    }
}
