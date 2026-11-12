using BaryoDev.Umbraco.MediumImport.Engine;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.MediumImport.Content;

public sealed record PropertyOption(string Alias, string Name, string EditorAlias);

public sealed record DocTypeOption(
    string Alias,
    string Name,
    IReadOnlyDictionary<string, IReadOnlyList<PropertyOption>> Slots);

/// <summary>Doc types allowed under a parent, with the properties that fit each import slot.</summary>
public sealed class MappingOptions
{
    private static readonly string[] Text = ["Umbraco.TextBox", "Umbraco.TextArea"];

    private readonly IContentService _content;
    private readonly IContentTypeService _types;

    public MappingOptions(IContentService content, IContentTypeService types)
    {
        _content = content;
        _types = types;
    }

    /// <summary>
    /// A plain reason when the settings cannot be used, or null when they are fine. Checks the mapping, the parent,
    /// that the document type exists and that it is allowed under the parent.
    /// </summary>
    public string? Validate(ImportSettings? settings)
    {
        if (settings is null)
        {
            return "Settings are required.";
        }

        if (settings.Mapping is null)
        {
            return "The field mapping is required.";
        }

        if (string.IsNullOrWhiteSpace(settings.Mapping.Title) || string.IsNullOrWhiteSpace(settings.Mapping.Body))
        {
            return "Map a title field and a body field.";
        }

        if (_content.GetById(settings.ParentKey) is null)
        {
            return "The parent page does not exist.";
        }

        if (string.IsNullOrWhiteSpace(settings.DocTypeAlias) || _types.Get(settings.DocTypeAlias) is null)
        {
            return $"The document type '{settings.DocTypeAlias}' does not exist.";
        }

        if (!For(settings.ParentKey).Any(o => o.Alias == settings.DocTypeAlias))
        {
            return $"The document type '{settings.DocTypeAlias}' is not allowed under the parent page.";
        }

        return null;
    }

    public IReadOnlyList<DocTypeOption> For(Guid parentKey)
    {
        IContent? parent = _content.GetById(parentKey);
        if (parent is null)
        {
            return [];
        }

        var result = new List<DocTypeOption>();
        IContentType? parentType = _types.Get(parent.ContentType.Key);
        foreach (ContentTypeSort sort in parentType?.AllowedContentTypes ?? [])
        {
            IContentType? type = _types.Get(sort.Alias);
            if (type is null)
            {
                continue;
            }

            IPropertyType[] props = type.CompositionPropertyTypes.ToArray();
            IReadOnlyList<PropertyOption> Pick(Func<string, bool> editor) =>
                props.Where(p => editor(p.PropertyEditorAlias))
                    .Select(p => new PropertyOption(p.Alias, p.Name, p.PropertyEditorAlias))
                    .ToList();

            var slots = new Dictionary<string, IReadOnlyList<PropertyOption>>
            {
                ["title"] = Pick(Text.Contains),
                ["body"] = Pick(e => e == "Umbraco.RichText"),
                ["publishDate"] = Pick(e => e.StartsWith("Umbraco.Date", StringComparison.Ordinal)),
                ["summary"] = Pick(Text.Contains),
                ["cover"] = Pick(e => e == "Umbraco.MediaPicker3"),
                ["tags"] = Pick(e => e == "Umbraco.Tags" || Text.Contains(e)),
                ["canonical"] = Pick(Text.Contains),
                ["originalUrl"] = Pick(Text.Contains),
            };
            result.Add(new DocTypeOption(type.Alias, type.Name ?? type.Alias, slots));
        }

        return result;
    }
}
