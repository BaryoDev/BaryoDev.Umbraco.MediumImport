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

    private static (HttpImageFetcher, FakeHandler) Fetcher(long maxImage = 1024)
    {
        var handler = new FakeHandler();
        var factory = new Factory(new HttpClient(handler));
        var options = Options.Create(new MediumImportOptions { MaxImageBytes = maxImage });
        return (new HttpImageFetcher(factory, options, NullLogger<HttpImageFetcher>.Instance), handler);
    }

    private static (GistClient, FakeHandler) Gists(long maxGist = 4096)
    {
        var handler = new FakeHandler();
        var factory = new Factory(new HttpClient(handler));
        var options = Options.Create(new MediumImportOptions { MaxGistBytes = maxGist });
        return (new GistClient(factory, options, NullLogger<GistClient>.Instance), handler);
    }

    private static readonly Uri Img = new("https://miro.medium.com/v2/resize:fit:700/1*abc.png");

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
            .Route("https://miro.medium.com/other/pic.png", () => FakeHandler.Bytes([1, 2, 3], "image/png"));
        FetchedImage? got = await fetcher.FetchAsync(Img, TestContext.Current.CancellationToken);
        Assert.NotNull(got);
        Assert.Equal("pic.png", got.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, got.Bytes);
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
        Assert.Equal("https://api.github.com/gists/abc123", handler.Requests.Single().AbsoluteUri);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("ABC")]
    [InlineData("")]
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
}
