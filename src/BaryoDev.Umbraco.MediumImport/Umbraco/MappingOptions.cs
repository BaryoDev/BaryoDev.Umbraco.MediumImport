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
