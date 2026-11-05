using System.Globalization;
using System.Text.Json.Nodes;
using HtmlAgilityPack;
using BaryoDev.Umbraco.MediumImport.Engine;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace BaryoDev.Umbraco.MediumImport.Content;

public sealed class UmbracoPostWriter : IPostWriter
{
    private const string SlugAlias = "umbracoUrlName";
    private const int PageSize = 200;

    private readonly IContentService _content;
    private readonly PropertyEditorCollection _editors;
    private readonly IDataTypeService _dataTypes;
    private readonly IJsonSerializer _json;

    public UmbracoPostWriter(
        IContentService content, PropertyEditorCollection editors, IDataTypeService dataTypes, IJsonSerializer json)
    {
        _content = content;
        _editors = editors;
        _dataTypes = dataTypes;
        _json = json;
    }

    public Guid? FindByOriginalUrl(ImportSettings settings, string url)
    {
        string? alias = settings.Mapping.OriginalUrl;
        IContent? parent = _content.GetById(settings.ParentKey);
        if (alias is null || parent is null)
        {
            return null;
        }

        long page = 0;
        while (true)
        {
            IContent[] children = _content.GetPagedChildren(parent.Id, page, PageSize, out long total, null, null, null, false).ToArray();
            foreach (IContent child in children)
            {
                if (child.ContentType.Alias == settings.DocTypeAlias
                    && child.HasProperty(alias)
                    && string.Equals(child.GetValue<string>(alias), url, StringComparison.Ordinal))
                {
                    return child.Key;
                }
            }

            page++;
            if (children.Length == 0 || page * PageSize >= total)
            {
                return null;
            }
        }
    }

    public IReadOnlyDictionary<string, string?>? ReadMapped(Guid contentKey, ImportSettings settings)
    {
        IContent? node = _content.GetById(contentKey);
        if (node is null || node.ContentType.Alias != settings.DocTypeAlias)
        {
            return null;
        }

        IContent? parent = _content.GetById(settings.ParentKey);
        if (parent is null || node.ParentId != parent.Id)
        {
            return null;
        }

        FieldMapping m = settings.Mapping;
        var result = new Dictionary<string, string?>();
        foreach (string? alias in new[] { m.Title, m.Body, m.PublishDate, m.Summary, m.Cover, m.Tags, m.Canonical, m.OriginalUrl })
        {
            if (alias is null)
            {
                continue;
            }

            object? v = node.HasProperty(alias) ? node.GetValue(alias) : null;
            result[alias] = v switch
            {
                null => null,
                string raw when alias == m.Body && IsRichText(node, alias) => NormaliseRichText(raw),
                DateTime d => d.ToString("o", CultureInfo.InvariantCulture),
                _ => Convert.ToString(v, CultureInfo.InvariantCulture),
            };
        }

        return result;
    }

    private static bool IsRichText(IContent node, string alias) =>
        node.Properties[alias]?.PropertyType.PropertyEditorAlias == "Umbraco.RichText";

    // The importer stores raw markup. A backoffice save stores a {"markup","blocks"} envelope and blanks
    // the src of any img that has a data-udi (Umbraco rebuilds it on read). Hash the markup with that src ignored.
    private static string NormaliseRichText(string raw)
    {
        string markup = raw;
        if (raw.TrimStart().StartsWith('{'))
        {
            try
            {
                if (JsonNode.Parse(raw) is JsonObject obj && obj["markup"] is JsonValue m && m.TryGetValue(out string? text))
                {
                    markup = text;
                }
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        if (!markup.Contains("data-udi", StringComparison.Ordinal))
        {
            return markup;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(markup);
        foreach (HtmlNode img in doc.DocumentNode.SelectNodes("//img[@data-udi]") ?? Enumerable.Empty<HtmlNode>())
        {
            if (!string.IsNullOrEmpty(img.GetAttributeValue("data-udi", "")))
            {
                img.Attributes.Remove("src");
            }
        }

        return doc.DocumentNode.OuterHtml;
    }

    public Guid Write(ImportSettings settings, PostValues values, Guid? existing, int userId)
    {
        IContent node;
        bool wasPublished = false;
        if (existing is null)
        {
            IContent parent = _content.GetById(settings.ParentKey)
                ?? throw new InvalidOperationException($"Parent {settings.ParentKey} not found.");
            node = _content.Create(values.Name, parent.Id, settings.DocTypeAlias, userId);
            if (!string.IsNullOrWhiteSpace(values.UrlSegment) && node.HasProperty(SlugAlias))
            {
                node.SetValue(SlugAlias, values.UrlSegment);
            }
        }
        else
        {
            node = _content.GetById(existing.Value)
                ?? throw new InvalidOperationException($"Post {existing} not found.");
            wasPublished = node.Published;
        }

        foreach (var (alias, value) in values.Values)
        {
            SetValue(node, alias, value);
        }

        if (settings.Mapping.Cover is not null && values.CoverMediaKey is Guid cover)
        {
            node.SetValue(settings.Mapping.Cover, CoverJson(cover));
        }

        _content.Save(node, userId);

        if ((existing is null && settings.Publish) || wasPublished)
        {
            var result = _content.Publish(node, ["*"], userId);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Could not publish '{values.Name}': {result.Result}");
            }
        }

        return node.Key;
    }

    private void SetValue(IContent node, string alias, object? value)
    {
        if (value is string[] tags)
        {
            if (node.Properties[alias]?.PropertyType.PropertyEditorAlias == "Umbraco.Tags")
            {
                node.AssignTags(_editors, _dataTypes, _json, alias, tags);
            }
            else
            {
                node.SetValue(alias, string.Join(",", tags));
            }

            return;
        }

        node.SetValue(alias, value);
    }

    private string CoverJson(Guid mediaKey) =>
        _json.Serialize(new[]
        {
            new
            {
                key = Guid.NewGuid(),
                mediaKey,
                mediaTypeAlias = "Image",
                crops = Array.Empty<object>(),
                focalPoint = (object?)null,
            },
        });
}
