using System.Net;
using BaryoDev.Umbraco.MediumImport.Net;
using BaryoDev.Umbraco.MediumImport.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.MediumImport.Tests;

public class NetTests
{
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static (HttpImageFetcher, FakeHandler) Fetcher(long maxImage = 1024, int timeout = 30)
    {
        var handler = new FakeHandler();
        var factory = new Factory(new HttpClient(handler));
        var options = Options.Create(new MediumImportOptions { MaxImageBytes = maxImage, HttpTimeoutSeconds = timeout });
        return (new HttpImageFetcher(factory, options, NullLogger<HttpImageFetcher>.Instance), handler);
    }

    private static (GistClient, FakeHandler) Gists(long maxGist = 4096, int timeout = 30)
    {
        var handler = new FakeHandler();
        var factory = new Factory(new HttpClient(handler));
        var options = Options.Create(new MediumImportOptions { MaxGistBytes = maxGist, HttpTimeoutSeconds = timeout });
        return (new GistClient(factory, options, NullLogger<GistClient>.Instance), handler);
    }

    private static readonly Uri Img = new("https://miro.medium.com/v2/resize:fit:700/1*abc.png");

    [Fact]
    public async Task Extensionless_jpeg_url_gets_jpg_extension()
    {
        var (fetcher, handler) = Fetcher();
        var url = new Uri("https://miro.medium.com/v2/resize:fit:800/0*vm7eSkm-3cgmZyIe");
        handler.Enqueue(() => FakeHandler.Bytes([0xFF, 0xD8, 0xFF, 0xE0, 0], "image/png"));
        FetchedImage? got = await fetcher.FetchAsync(url, TestContext.Current.CancellationToken);
        Assert.NotNull(got);
        Assert.EndsWith(".jpg", got.FileName);
    }

    [Fact]
    public async Task Extensionless_png_url_gets_png_extension()
    {
        var (fetcher, handler) = Fetcher();
        var url = new Uri("https://miro.medium.com/v2/resize:fit:800/1*noext");
        handler.Enqueue(() => FakeHandler.Bytes([0x89, 0x50, 0x4E, 0x47, 0x0D], "image/png"));
        FetchedImage? got = await fetcher.FetchAsync(url, TestContext.Current.CancellationToken);
        Assert.NotNull(got);
        Assert.EndsWith(".png", got.FileName);
    }

    [Fact]
    public async Task Fetch_refuses_other_hosts_without_a_request()
    {
        var (fetcher, handler) = Fetcher();
        Assert.Null(await fetcher.FetchAsync(new Uri("https://example.com/a.png"), TestContext.Current.CancellationToken));
        Assert.Null(await fetcher.FetchAsync(new Uri("http://miro.medium.com/a.png"), TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Fetch_follows_redirect_to_medium_host()
    {
        var (fetcher, handler) = Fetcher();
        handler.Route(Img.AbsoluteUri, () => FakeHandler.Redirect("/other/pic.png"))
            .Route("https://miro.medium.com/other/pic.png", () => FakeHandler.Bytes([0x89, 0x50, 0x4E, 0x47, 1], "image/png"));
        FetchedImage? got = await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken);
        Assert.NotNull(got);
        Assert.Equal("pic.png", got.FileName);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1 }, got.Bytes);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Fetch_refuses_redirect_to_other_host()
    {
        var (fetcher, handler) = Fetcher();
        handler.Route(Img.AbsoluteUri, () => FakeHandler.Redirect("https://evil.example/x.png"));
        Assert.Null(await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Fetch_refuses_oversize()
    {
        var (fetcher, handler) = Fetcher(maxImage: 10);
        handler.Enqueue(() => FakeHandler.Bytes(new byte[50], "image/png", declareLength: false));
        Assert.Null(await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken));
        handler.Enqueue(() => FakeHandler.Bytes(new byte[50], "image/png"));
        Assert.Null(await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_refuses_non_image()
    {
        var (fetcher, handler) = Fetcher();
        handler.Enqueue(() => FakeHandler.Bytes([1], "text/html"));
        Assert.Null(await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("image/svg+xml", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("image/png", "<?xml version=\"1.0\"?><svg onload=\"alert(1)\"></svg>")]
    [InlineData("image/avif", "....ftypavif")]
    public async Task Svg_image_is_rejected(string contentType, string body)
    {
        var (fetcher, handler) = Fetcher();
        var url = new Uri("https://miro.medium.com/v2/resize:fit:700/1*logo.svg");
        handler.Enqueue(() => FakeHandler.Bytes(System.Text.Encoding.UTF8.GetBytes(body), contentType));
        Assert.Null(await fetcher.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Gist_maps_files_in_order()
    {
        var (client, handler) = Gists();
        handler.Enqueue(() => FakeHandler.Json("""
            {"html_url":"https://gist.github.com/u/abc123","files":{
              "b.py":{"filename":"b.py","language":"Python","content":"print(1)"},
              "a.txt":{"filename":"a.txt","language":null,"content":"hi"},
              "c.cs":{"filename":"c.cs","language":"C#","content":"var x = 1;"}}}
            """));
        var gist = await client.GetAsync("abc123", TestContext.Current.CancellationToken);
        Assert.NotNull(gist);
        Assert.Equal(["b.py", "a.txt", "c.cs"], gist.Files.Select(f => f.Name));
        Assert.Equal("print(1)", gist.Files[0].Content);
        Assert.Equal(["python", null, "c#"], gist.Files.Select(f => f.Language));
        Assert.Equal("https://gist.github.com/u/abc123", gist.HtmlUrl.AbsoluteUri);
        Assert.Equal("https://api.github.com/gists/abc123", handler.Requests.Single().AbsoluteUri);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("ABC")]
    [InlineData("")]
    [InlineData("abc\n")]
    [InlineData("abc/def")]
    public async Task Gist_bad_id_makes_no_request(string id)
    {
        var (client, handler) = Gists();
        Assert.Null(await client.GetAsync(id, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Gist_404_returns_null()
    {
        var (client, handler) = Gists();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await client.GetAsync("abc123", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Gist_truncated_returns_null()
    {
        var (client, handler) = Gists();
        handler.Enqueue(() => FakeHandler.Json("""{"files":{"a.txt":{"filename":"a.txt","content":"x","truncated":true}}}"""));
        Assert.Null(await client.GetAsync("abc123", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Gist_oversize_returns_null()
    {
        var (client, handler) = Gists(maxGist: 20);
        handler.Enqueue(() => FakeHandler.Json("""{"files":{"a.txt":{"filename":"a.txt","content":"0123456789012345678901234567890"}}}"""));
        Assert.Null(await client.GetAsync("abc123", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://gist.github.com/u/abc123")]
    [InlineData("https://evil.example/u/abc123")]
    public async Task Gist_foreign_html_url_falls_back(string htmlUrl)
    {
        var (client, handler) = Gists();
        handler.Enqueue(() => FakeHandler.Json(
            "{\"html_url\":\"" + htmlUrl + "\",\"files\":{\"a.txt\":{\"filename\":\"a.txt\",\"content\":\"x\"}}}"));
        var gist = await client.GetAsync("abc123", TestContext.Current.CancellationToken);
        Assert.NotNull(gist);
        Assert.Equal("https://gist.github.com/abc123", gist.HtmlUrl.AbsoluteUri);
    }

    [Fact]
    public async Task Slow_body_times_out_to_null()
    {
        var (fetcher, ih) = Fetcher(timeout: 1);
        ih.Enqueue(() => FakeHandler.StreamContent2.Response(() => new FakeHandler.BlockingStream(), null, "image/png"));
        Assert.Null(await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken));

        var (client, gh) = Gists(timeout: 1);
        gh.Enqueue(() => FakeHandler.StreamContent2.Response(() => new FakeHandler.BlockingStream(), null, "application/json"));
        Assert.Null(await client.GetAsync("abc123", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Caller_cancel_still_throws()
    {
        var (fetcher, handler) = Fetcher();
        handler.Enqueue(() => FakeHandler.StreamContent2.Response(() => new FakeHandler.BlockingStream(), null, "image/png"));
        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchAsync(Img, cts.Token));
    }

    [Fact]
    public async Task Body_io_error_gives_null()
    {
        var (fetcher, ih) = Fetcher();
        ih.Enqueue(() => FakeHandler.StreamContent2.Response(() => new FakeHandler.ThrowingStream(), null, "image/png"));
        Assert.Null(await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken));

        var (client, gh) = Gists();
        gh.Enqueue(() => FakeHandler.StreamContent2.Response(() => new FakeHandler.ThrowingStream(), null, "application/json"));
        Assert.Null(await client.GetAsync("abc123", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Lying_content_length_is_still_capped()
    {
        var (fetcher, ih) = Fetcher(maxImage: 10);
        ih.Enqueue(() => FakeHandler.StreamContent2.Response(() => new MemoryStream(new byte[50]), 5, "image/png"));
        Assert.Null(await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken));

        var (client, gh) = Gists(maxGist: 10);
        gh.Enqueue(() => FakeHandler.StreamContent2.Response(() => new MemoryStream(new byte[50]), 5, "application/json"));
        Assert.Null(await client.GetAsync("abc123", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fourth_redirect_returns_null()
    {
        var (fetcher, handler) = Fetcher();
        handler.Route(Img.AbsoluteUri, () => FakeHandler.Redirect("/r1.png"))
            .Route("https://miro.medium.com/r1.png", () => FakeHandler.Redirect("/r2.png"))
            .Route("https://miro.medium.com/r2.png", () => FakeHandler.Redirect("/r3.png"))
            .Route("https://miro.medium.com/r3.png", () => FakeHandler.Redirect("/r4.png"))
            .Route("https://miro.medium.com/r4.png", () => FakeHandler.Bytes([1], "image/png"));
        Assert.Null(await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken));
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Redirect_to_http_medium_url_returns_null()
    {
        var (fetcher, handler) = Fetcher();
        handler.Route(Img.AbsoluteUri, () => FakeHandler.Redirect("http://miro.medium.com/x.png"));
        Assert.Null(await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }
}
