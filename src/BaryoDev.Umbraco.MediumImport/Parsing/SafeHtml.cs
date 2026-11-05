using HtmlAgilityPack;

namespace BaryoDev.Umbraco.MediumImport.Parsing;

/// <summary>The one place the package parses HTML it does not control. The depth cap keeps every later recursive walk off the stack limit.</summary>
internal static class SafeHtml
{
    public const int MaxDepth = 512;

    public static HtmlDocument Load(string html)
    {
        var doc = new HtmlDocument { OptionMaxNestedChildNodes = MaxDepth };
        try
        {
            doc.LoadHtml(html);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // HtmlAgilityPack reports a breach of the depth cap with a plain Exception.
            throw new HtmlTooDeepException();
        }

        return doc;
    }
}
