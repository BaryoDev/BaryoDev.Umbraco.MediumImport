using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

/// <summary>Creates the post and blog types and a published blog root, shared by the import and API fixtures.</summary>
public static class TypeSeeder
{
    public static Guid Seed(IServiceProvider services)
    {
        var textBox = DataType(services, "Umbraco.TextBox");
        var textArea = DataType(services, "Umbraco.TextArea");
        var richText = DataType(services, "Umbraco.RichText");
        var date = DataType(services, "Umbraco.DateTime");
        var picker = DataType(services, "Umbraco.MediaPicker3");
        var tags = DataType(services, "Umbraco.Tags");

        CreateType(services, "post", false, [], new (string, IDataType)[]
        {
            ("title", textBox), ("body", richText), ("publishDate", date), ("summary", textArea),
            ("cover", picker), ("tags", tags), ("canonical", textBox), ("originalUrl", textBox),
            ("headline", textBox), ("umbracoUrlName", textBox), ("tagsText", textBox),
        });
        CreateType(services, "bare", false, [], new (string, IDataType)[] { ("title", textBox), ("body", richText) });
        CreateType(services, "blog", true, ["post", "bare"], new (string, IDataType)[] { ("title", textBox) });

        var content = services.GetRequiredService<IContentService>();
        IContent blog = content.Create("Blog", Constants.System.Root, "blog");
        content.Save(blog);
        var published = content.Publish(blog, ["*"]);
        Assert.That(published.Success, Is.True, "publish blog: " + published.Result);
        return blog.Key;
    }

    private static IDataType DataType(IServiceProvider services, string editorAlias)
    {
        var editors = services.GetRequiredService<PropertyEditorCollection>();
        Assert.That(editors.TryGet(editorAlias, out IDataEditor? editor), Is.True, editorAlias);
        var dt = new DataType(editor!, services.GetRequiredService<IConfigurationEditorJsonSerializer>()) { Name = "mi-" + editorAlias };
        var result = services.GetRequiredService<IDataTypeService>().CreateAsync(dt, Constants.Security.SuperUserKey).GetAwaiter().GetResult();
        Assert.That(result.Success, Is.True, editorAlias + " " + result.Status);
        return result.Result;
    }

    private static void CreateType(IServiceProvider services, string alias, bool root, string[] allowed, (string Alias, IDataType Type)[] props)
    {
        var shortString = services.GetRequiredService<IShortStringHelper>();
        var types = services.GetRequiredService<IContentTypeService>();
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
