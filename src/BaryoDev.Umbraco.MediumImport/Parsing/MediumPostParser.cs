using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace BaryoDev.Umbraco.MediumImport.Parsing;

/// <summary>Parses one post file from a Medium export. BodyHtml is raw and must go through the sanitizer.</summary>
public static class MediumPostParser
{
    private static readonly Regex IdPattern = new(@"-([0-9a-f]{8,16})\.html$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GistPattern = new(@"^https?://gist\.github\.com/[^/?#]+/([0-9a-f]+)(?:\.js)?(?:[?#].*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool HasPostId(string fileName) => IdPattern.IsMatch(fileName);

    public static MediumStory Parse(string fileName, string html)
    {
        Match idMatch = IdPattern.Match(fileName);
        string postId = idMatch.Success ? idMatch.Groups[1].Value.ToLowerInvariant() : "";

        HtmlDocument doc;
        try
        {
            doc = SafeHtml.Load(html);
        }
        catch (HtmlTooDeepException ex)
        {
            return new MediumStory(postId, fileName, "", null, null, null, [], "", [], [], null, false, false) { ParseFailure = ex.Message };
        }

        string? h1 = Text(Find(doc, "h1", "p-name"));
        string title = !string.IsNullOrWhiteSpace(h1) ? h1 : Text(doc.DocumentNode.SelectSingleNode("//title")) ?? "";
        string? summary = Text(doc.DocumentNode.Descendants("section").FirstOrDefault(n => HasClass(n, "p-summary")));
        if (string.IsNullOrEmpty(summary))
        {
            summary = null;
        }

        HtmlNode? time = Find(doc, "time", "dt-published");
        DateTime? published = null;
        if (time is not null
            && DateTime.TryParse(time.GetAttributeValue("datetime", ""), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime dt))
        {
            published = dt;
        }

        Uri? mediumUrl = null;
        string href = HtmlEntity.DeEntitize(Find(doc, "a", "p-canonical")?.GetAttributeValue("href", "") ?? "");
        if (Uri.TryCreate(href, UriKind.Absolute, out Uri? c) && c.Scheme == Uri.UriSchemeHttps)
        {
            mediumUrl = c;
        }

        HtmlNode? body = doc.DocumentNode.SelectSingleNode("//section[@data-field='body']");

        bool hasTitleHeading = false;
        string? subtitle = null;
        if (body is not null)
        {
            foreach (HtmlNode n in body.Descendants().Where(n => HasClass(n, "graf--title") || HasClass(n, "graf--subtitle")).ToList())
            {
                if (HasClass(n, "graf--title"))
                {
                    hasTitleHeading = true;
                }
                else
                {
                    subtitle ??= Text(n);
                }

                n.Remove();
            }
        }

        var images = new List<Uri>();
        var gists = new List<string>();
        foreach (HtmlNode n in body?.Descendants() ?? Enumerable.Empty<HtmlNode>())
        {
            if (n.Name == "img"
                && Uri.TryCreate(HtmlEntity.DeEntitize(n.GetAttributeValue("src", "")), UriKind.Absolute, out Uri? u)
                && u.Scheme == Uri.UriSchemeHttps && !images.Contains(u))
            {
                images.Add(u);
            }

            string? link = n.Name == "a" ? n.GetAttributeValue("href", null)
                : n.Name == "script" ? n.GetAttributeValue("src", null) : null;
            if (link is not null)
            {
                Match g = GistPattern.Match(HtmlEntity.DeEntitize(link));
                if (g.Success && !gists.Contains(g.Groups[1].Value, StringComparer.OrdinalIgnoreCase))
                {
                    gists.Add(g.Groups[1].Value);
                }
            }
        }

        var tags = doc.DocumentNode.Descendants("a")
            .Where(a => HasClass(a, "p-tag") || a.GetAttributeValue("rel", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("tag", StringComparer.OrdinalIgnoreCase))
            .Select(Text).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        bool isReply = doc.DocumentNode.Descendants("a").Any(a => HasClass(a, "u-in-reply-to"));
        bool isDraft = fileName.StartsWith("draft", StringComparison.OrdinalIgnoreCase) || published is null;

        return new MediumStory(postId, fileName, title, subtitle, summary, published, tags,
            body?.InnerHtml ?? "", images, gists, mediumUrl, isDraft, isReply || !hasTitleHeading);
    }

    private static HtmlNode? Find(HtmlDocument doc, string tag, string cls) =>
        doc.DocumentNode.Descendants(tag).FirstOrDefault(n => HasClass(n, cls));

    private static bool HasClass(HtmlNode n, string cls) =>
        n.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(cls);

    private static string? Text(HtmlNode? n) =>
        n is null ? null : HtmlEntity.DeEntitize(n.InnerText).Trim();
}
