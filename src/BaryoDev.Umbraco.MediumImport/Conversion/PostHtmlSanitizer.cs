using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BaryoDev.Umbraco.MediumImport.Net;
using HtmlAgilityPack;

namespace BaryoDev.Umbraco.MediumImport.Conversion;

/// <summary>
/// Rebuilds imported HTML from an allowlist. Tags outside the list are unwrapped (their text stays),
/// script, style, iframe and similar are dropped with their content, and only the listed attributes survive.
/// The output is written by hand with encoded text and attribute values, never copied from the input.
/// With a context, code blocks, gists and Medium images are written too. The only attribute added
/// to the allowlist is class="language-*", and only on code.
/// </summary>
public static class PostHtmlSanitizer
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "h2", "h3", "h4", "h5", "h6", "p", "a", "em", "strong", "code", "pre", "blockquote",
        "ul", "ol", "li", "figure", "img", "figcaption", "br",
    };

    private static readonly HashSet<string> DropWithContent = new(StringComparer.Ordinal)
    {
        "script", "style", "iframe", "object", "embed", "noscript", "template", "svg", "math", "form",
        "head", "title", "link", "meta", "base", "textarea", "select", "button", "frame", "frameset",
    };

    private static readonly HashSet<string> Void = new(StringComparer.Ordinal) { "img", "br" };

    private static readonly Regex GistPattern = new(
        @"^https?://gist\.github\.com/[^/?#]+/([0-9a-f]+)(?:\.js)?(?:[?#].*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Sanitize(string html, SanitizeContext? context = null)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        EmbedsToLinks(doc, context is not null);
        var sb = new StringBuilder();
        Walk(doc.DocumentNode, sb, context);
        return sb.ToString().Trim();
    }

    /// <summary>A gist script or an https iframe becomes a plain link. Everything else that runs is dropped later.</summary>
    private static void EmbedsToLinks(HtmlDocument doc, bool withContext)
    {
        foreach (HtmlNode node in doc.DocumentNode.Descendants().Where(n => n.Name is "script" or "iframe").ToList())
        {
            if (withContext && node.Ancestors("pre").Any())
            {
                node.Remove();
                continue;
            }

            string src = HtmlEntity.DeEntitize(node.GetAttributeValue("src", "")).Trim();
            if (!Uri.TryCreate(src, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || node.ParentNode is null)
            {
                continue;
            }

            bool gist = uri.Host.Equals("gist.github.com", StringComparison.OrdinalIgnoreCase);
            if (node.Name == "script" && !gist)
            {
                continue;
            }

            string href = gist && uri.AbsolutePath.EndsWith(".js", StringComparison.Ordinal)
                ? new UriBuilder(uri) { Path = uri.AbsolutePath[..^3], Query = "" }.Uri.AbsoluteUri
                : uri.AbsoluteUri;
            string label = gist ? "View the code on GitHub" : "Open " + uri.Host;
            string anchor = "<a href=\"" + WebUtility.HtmlEncode(href) + "\">" + WebUtility.HtmlEncode(label) + "</a>";
            bool inParagraph = withContext && node.ParentNode.Name.Equals("p", StringComparison.OrdinalIgnoreCase);
            HtmlNode link = HtmlNode.CreateNode(inParagraph ? anchor : "<p>" + anchor + "</p>");
            node.ParentNode.ReplaceChild(link, node);
        }
    }

    private static void Walk(HtmlNode parent, StringBuilder sb, SanitizeContext? ctx)
    {
        foreach (HtmlNode node in parent.ChildNodes)
        {
            switch (node.NodeType)
            {
                case HtmlNodeType.Text:
                    sb.Append(WebUtility.HtmlEncode(HtmlEntity.DeEntitize(node.InnerText)));
                    break;
                case HtmlNodeType.Element:
                    Element(node, sb, ctx);
                    break;
            }
        }
    }

    private static void Element(HtmlNode node, StringBuilder sb, SanitizeContext? ctx)
    {
        string name = node.Name.ToLowerInvariant();
        if (DropWithContent.Contains(name))
        {
            return;
        }

        if (name == "h1")
        {
            name = "h2";
        }

        if (!Allowed.Contains(name))
        {
            Walk(node, sb, ctx);
            return;
        }

        if (name == "img")
        {
            Image(node, sb, ctx);
            return;
        }

        if (ctx is not null)
        {
            if (name == "pre")
            {
                CodeBlock(CodeLanguage.Normalize(HtmlEntity.DeEntitize(node.GetAttributeValue("data-code-block-lang", ""))), CodeText(node), sb);
                return;
            }

            if (name == "figure" && DropsAllImages(node, ctx))
            {
                return;
            }

            if (name == "p" && Paragraph(node, sb, ctx))
            {
                return;
            }
        }

        StringBuilder? inner = null;
        if (ctx is not null && name is "a" or "p")
        {
            inner = new StringBuilder();
            Walk(node, inner, ctx);
            if (inner.Length == 0)
            {
                return;
            }
        }

        sb.Append('<').Append(name);
        if (name == "a")
        {
            string? href = Safe(node.GetAttributeValue("href", ""), allowRelative: true, allowMailto: true);
            if (href is not null)
            {
                sb.Append(" href=\"").Append(WebUtility.HtmlEncode(href)).Append('"');
                if (href.StartsWith("http", StringComparison.Ordinal))
                {
                    sb.Append(" rel=\"noopener\"");
                }
            }
        }
        else if (name == "code" && ctx is not null)
        {
            string? language = LanguageClass(node);
            if (language is not null)
            {
                sb.Append(" class=\"language-").Append(WebUtility.HtmlEncode(language)).Append('"');
            }
        }

        sb.Append('>');
        if (Void.Contains(name))
        {
            return;
        }

        if (inner is not null)
        {
            sb.Append(inner);
        }
        else
        {
            Walk(node, sb, ctx);
        }

        sb.Append("</").Append(name).Append('>');
    }

    private static void Image(HtmlNode node, StringBuilder sb, SanitizeContext? ctx)
    {
        string? src = Safe(node.GetAttributeValue("src", ""), allowRelative: true);
        if (src is null)
        {
            return;
        }

        string alt = WebUtility.HtmlEncode(HtmlEntity.DeEntitize(node.GetAttributeValue("alt", "")));
        if (ctx is not null && Uri.TryCreate(src, UriKind.Absolute, out Uri? uri) && MediumHosts.IsMediumImage(uri))
        {
            if (!ctx.Images.TryGetValue(uri, out ResolvedImage? image))
            {
                return;
            }

            string? url = Safe(image.Url, allowRelative: true);
            if (url is null)
            {
                return;
            }

            sb.Append("<img src=\"").Append(WebUtility.HtmlEncode(url))
                .Append("\" data-udi=\"umb://media/").Append(image.MediaKey.ToString("N"))
                .Append("\" alt=\"").Append(alt).Append("\">");
            return;
        }

        sb.Append("<img src=\"").Append(WebUtility.HtmlEncode(src)).Append("\" alt=\"").Append(alt).Append("\">");
    }

    /// <summary>True when the figure holds images and every one is a Medium image that was not resolved.</summary>
    private static bool DropsAllImages(HtmlNode figure, SanitizeContext ctx)
    {
        List<HtmlNode> images = figure.Descendants("img").ToList();
        return images.Count > 0 && images.All(img =>
        {
            string? src = Safe(img.GetAttributeValue("src", ""), allowRelative: true);
            return src is null
                || (Uri.TryCreate(src, UriKind.Absolute, out Uri? uri)
                    && MediumHosts.IsMediumImage(uri)
                    && !ctx.Images.ContainsKey(uri));
        });
    }

    private static void CodeBlock(string? language, string text, StringBuilder sb)
    {
        sb.Append("<pre><code");
        if (language is not null)
        {
            sb.Append(" class=\"language-").Append(WebUtility.HtmlEncode(language)).Append('"');
        }

        sb.Append('>').Append(WebUtility.HtmlEncode(text)).Append("</code></pre>");
    }

    /// <summary>The text of a code block: br is a newline, every other tag drops and keeps its text.</summary>
    private static string CodeText(HtmlNode node)
    {
        var text = new StringBuilder();
        AppendCodeText(node, text);
        return text.ToString();
    }

    private static void AppendCodeText(HtmlNode parent, StringBuilder text)
    {
        foreach (HtmlNode node in parent.ChildNodes)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                text.Append(HtmlEntity.DeEntitize(node.InnerText));
            }
            else if (node.NodeType == HtmlNodeType.Element)
            {
                string name = node.Name.ToLowerInvariant();
                if (name == "br")
                {
                    text.Append('\n');
                }
                else if (!DropWithContent.Contains(name))
                {
                    AppendCodeText(node, text);
                }
            }
        }
    }

    private static string? LanguageClass(HtmlNode code)
    {
        string classes = HtmlEntity.DeEntitize(code.GetAttributeValue("class", ""));
        foreach (string token in classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("language-", StringComparison.Ordinal))
            {
                return CodeLanguage.Normalize(token["language-".Length..]);
            }
        }

        return null;
    }

    /// <summary>Writes a paragraph that holds a known gist link. Returns false when the paragraph is not one.</summary>
    private static bool Paragraph(HtmlNode p, StringBuilder sb, SanitizeContext ctx)
    {
        var gists = new List<Gist>();
        foreach (HtmlNode a in p.Descendants("a"))
        {
            Match match = GistPattern.Match(Safe(a.GetAttributeValue("href", ""), allowRelative: false) ?? "");
            if (match.Success
                && ctx.Gists.TryGetValue(match.Groups[1].Value.ToLowerInvariant(), out Gist? gist)
                && !gists.Contains(gist))
            {
                gists.Add(gist);
            }
        }

        if (gists.Count == 0)
        {
            return false;
        }

        List<HtmlNode> content = p.ChildNodes
            .Where(n => n.NodeType == HtmlNodeType.Element
                || (n.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(HtmlEntity.DeEntitize(n.InnerText))))
            .ToList();
        bool alone = content.Count == 1 && content[0].Name.Equals("a", StringComparison.OrdinalIgnoreCase);

        if (!alone)
        {
            sb.Append("<p>");
            Walk(p, sb, ctx);
            sb.Append("</p>");
        }

        foreach (Gist gist in gists)
        {
            foreach (GistFile file in gist.Files)
            {
                CodeBlock(CodeLanguage.Normalize(file.Language) ?? CodeLanguage.FromFileName(file.Name), file.Content, sb);
            }

            if (alone && (gist.HtmlUrl.Scheme == Uri.UriSchemeHttps || gist.HtmlUrl.Scheme == Uri.UriSchemeHttp))
            {
                sb.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(gist.HtmlUrl.AbsoluteUri))
                    .Append("\">Source: gist</a></p>");
            }
        }

        return true;
    }

    /// <summary>Returns the URL when it is http, https, a site path, a fragment or (when allowed) mailto. Otherwise null.</summary>
    private static string? Safe(string raw, bool allowRelative, bool allowMailto = false)
    {
        string url = HtmlEntity.DeEntitize(raw).Trim();
        if (url.Length == 0 || url.Any(char.IsControl))
        {
            return null;
        }

        if (url.StartsWith("//", StringComparison.Ordinal) || url.StartsWith("/\\", StringComparison.Ordinal))
        {
            return null;
        }

        if (allowRelative && (url.StartsWith('/') || url.StartsWith('#')))
        {
            return url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? abs))
        {
            if (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps)
            {
                return abs.AbsoluteUri;
            }

            if (allowMailto && abs.Scheme == Uri.UriSchemeMailto)
            {
                return url;
            }
        }

        return null;
    }
}
