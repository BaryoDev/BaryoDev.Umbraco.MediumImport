using BaryoDev.Umbraco.MediumImport.Conversion;

namespace BaryoDev.Umbraco.MediumImport.Tests;

public class SanitizerTests
{
    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"jav&#x61;script:alert(1)\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"jav&#9;ascript:alert(1)\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"//evil.example/x\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"https://example.com/x\" onclick=\"y()\" target=\"_blank\">x</a>", "<a href=\"https://example.com/x\" rel=\"noopener\">x</a>")]
    [InlineData("<a href=\"/blog/\">x</a>", "<a href=\"/blog/\">x</a>")]
    [InlineData("<img src=\"javascript:alert(1)\" alt=\"a\">", "")]
    [InlineData("<img src=\"JaVaScRiPt:alert(1)\" alt=\"a\">", "")]
    [InlineData("<img src=\"jav&#x61;script:alert(1)\" alt=\"a\">", "")]
    [InlineData("<img src=\"data:image/svg+xml;base64,PHN2Zz4=\" alt=\"a\">", "")]
    [InlineData("<img src=\"//evil.example/a.png\" alt=\"a\">", "")]
    [InlineData("<img src=\"https://example.com/a.png\" alt=\"a\" onerror=\"y()\">", "<img src=\"https://example.com/a.png\" alt=\"a\">")]
    [InlineData("<p style=\"color:red\" class=\"c\" onclick=\"y()\">hi</p>", "<p>hi</p>")]
    [InlineData("<p>a</p><object data=\"x.swf\">fallback</object><p>b</p>", "<p>a</p><p>b</p>")]
    [InlineData("<p>a</p><embed src=\"x.swf\"><p>b</p>", "<p>a</p><p>b</p>")]
    [InlineData("<p>a</p><svg><g>t</g></svg><p>b</p>", "<p>a</p><p>b</p>")]
    [InlineData("<p>a</p><math><mi>t</mi></math><p>b</p>", "<p>a</p><p>b</p>")]
    [InlineData("<p>a</p><style>p{display:none}</style><p>b</p>", "<p>a</p><p>b</p>")]
    [InlineData("<p>a</p><script>alert(1)</script><p>b</p>", "<p>a</p><p>b</p>")]
    [InlineData("<h1>t</h1>", "<h2>t</h2>")]
    public void Sanitize_ReturnsExactOutput(string input, string expected) =>
        Assert.Equal(expected, PostHtmlSanitizer.Sanitize(input));
}
