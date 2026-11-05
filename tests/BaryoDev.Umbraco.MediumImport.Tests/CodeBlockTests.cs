using BaryoDev.Umbraco.MediumImport.Conversion;

namespace BaryoDev.Umbraco.MediumImport.Tests;

public class CodeBlockTests
{
    private static readonly Guid MediaKey = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Uri GistUrl = new("https://gist.github.com/someone/0123456789abcdef");
    private const string GistLink = "<a href=\"https://gist.github.com/someone/0123456789abcdef\">https://gist.github.com/someone/0123456789abcdef</a>";

    private static SanitizeContext Context(bool withGist = true, bool withImage = true) => new(
        withImage
            ? new Dictionary<Uri, ResolvedImage>
            {
                [new Uri("https://cdn-images-1.medium.com/max/800/1*one.png")] = new("/media/one.png", MediaKey),
            }
            : new Dictionary<Uri, ResolvedImage>(),
        withGist
            ? new Dictionary<string, Gist>
            {
                ["0123456789abcdef"] = new("0123456789abcdef", GistUrl, new[]
                {
                    new GistFile("main.go", null, "a < b && c"),
                    new GistFile("notes.txt", "Plain Text", "x"),
                }),
            }
            : new Dictionary<string, Gist>());

    [Fact]
    public void Br_becomes_newline_and_spans_drop()
    {
        string html = "<pre data-code-block-lang=\"go\"><span class=\"hljs-keyword\">func</span> main() {<br />    run()<br />    <br />}</pre>";
        Assert.Equal(
            "<pre><code class=\"language-go\">func main() {\n    run()\n    \n}</code></pre>",
            PostHtmlSanitizer.Sanitize(html, Context()));
    }

    [Fact]
    public void Text_is_encoded_never_copied()
    {
        string html = "<pre>a &lt;script&gt;x&lt;/script&gt; <span><script>alert(1)</script></span>b</pre>";
        string result = PostHtmlSanitizer.Sanitize(html, Context());
        Assert.Equal("<pre><code>a &lt;script&gt;x&lt;/script&gt; b</code></pre>", result);
        Assert.DoesNotContain("<script", result);
    }

    [Theory]
    [InlineData("c c", "language-c-c")]
    [InlineData("<x>", null)]
    [InlineData("a\" onclick=\"y()", null)]
    public void Unknown_language_gets_no_class(string lang, string? expectedClass)
    {
        string html = "<pre data-code-block-lang=\"" + System.Net.WebUtility.HtmlEncode(lang) + "\">t</pre>";
        string expected = expectedClass is null
            ? "<pre><code>t</code></pre>"
            : "<pre><code class=\"" + expectedClass + "\">t</code></pre>";
        Assert.Equal(expected, PostHtmlSanitizer.Sanitize(html, Context()));
    }

    [Fact]
    public void Adjacent_blocks_stay_separate()
    {
        Assert.Equal(
            "<pre><code class=\"language-go\">a</code></pre><pre><code>b</code></pre>",
            PostHtmlSanitizer.Sanitize("<pre data-code-block-lang=\"go\">a</pre><pre>b</pre>", Context()));
    }

    [Fact]
    public void Inline_code_stays_code()
    {
        Assert.Equal("<p>use <code>x &lt; y</code> here</p>", PostHtmlSanitizer.Sanitize("<p>use <code>x &lt; y</code> here</p>", Context()));
    }

    [Fact]
    public void Gist_alone_in_paragraph_is_replaced()
    {
        Assert.Equal(
            "<pre><code class=\"language-go\">a &lt; b &amp;&amp; c</code></pre>"
            + "<pre><code class=\"language-plain-text\">x</code></pre>"
            + "<p><a href=\"https://gist.github.com/someone/0123456789abcdef\">Source: gist</a></p>",
            PostHtmlSanitizer.Sanitize("<p>" + GistLink + "</p>", Context()));
    }

    [Fact]
    public void Gist_in_running_text_keeps_link_and_appends_blocks()
    {
        string result = PostHtmlSanitizer.Sanitize("<p>See " + GistLink + " now.</p>", Context());
        Assert.Equal(
            "<p>See <a href=\"https://gist.github.com/someone/0123456789abcdef\" rel=\"noopener\">https://gist.github.com/someone/0123456789abcdef</a> now.</p>"
            + "<pre><code class=\"language-go\">a &lt; b &amp;&amp; c</code></pre>"
            + "<pre><code class=\"language-plain-text\">x</code></pre>",
            result);
    }

    [Fact]
    public void Gist_missing_from_context_keeps_link()
    {
        Assert.Equal(
            "<p><a href=\"https://gist.github.com/someone/0123456789abcdef\" rel=\"noopener\">https://gist.github.com/someone/0123456789abcdef</a></p>",
            PostHtmlSanitizer.Sanitize("<p>" + GistLink + "</p>", Context(withGist: false)));
    }

    [Fact]
    public void Medium_image_rewritten_with_udi()
    {
        Assert.Equal(
            "<figure><img src=\"/media/one.png\" data-udi=\"umb://media/11111111222233334444555555555555\" alt=\"one\"><figcaption>First</figcaption></figure>",
            PostHtmlSanitizer.Sanitize(
                "<figure><img src=\"https://cdn-images-1.medium.com/max/800/1*one.png\" alt=\"one\"><figcaption>First</figcaption></figure>",
                Context()));
    }

    [Fact]
    public void Unresolved_medium_image_dropped()
    {
        Assert.Equal(
            "<p>a</p><p>b</p>",
            PostHtmlSanitizer.Sanitize(
                "<p>a</p><figure><img src=\"https://miro.medium.com/v2/1*two.png\" alt=\"two\"><figcaption>Gone</figcaption></figure><p>b</p>",
                Context()));
    }

    [Fact]
    public void Other_host_image_kept()
    {
        Assert.Equal(
            "<figure><img src=\"https://example.com/pic.png\" alt=\"elsewhere\"></figure>",
            PostHtmlSanitizer.Sanitize("<figure><img src=\"https://example.com/pic.png\" alt=\"elsewhere\"></figure>", Context()));
    }

    [Fact]
    public void Language_class_allowed_only_on_code()
    {
        Assert.Equal("<p>x</p>", PostHtmlSanitizer.Sanitize("<p class=\"language-go\">x</p>", Context()));
        Assert.Equal(
            "<p><code class=\"language-go\">x</code></p>",
            PostHtmlSanitizer.Sanitize("<p><code class=\"language-go\">x</code></p>", Context()));
        Assert.Equal(
            "<p><code>x</code></p>",
            PostHtmlSanitizer.Sanitize("<p><code class=\"language-&lt;x&gt;\">x</code></p>", Context()));
    }
}
