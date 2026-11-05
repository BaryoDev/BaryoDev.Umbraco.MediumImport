using System.Net;

namespace BaryoDev.Umbraco.MediumImport.Tests.Fakes;

/// <summary>Records requests and answers from a route table or a queue.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new();
    private readonly Queue<Func<HttpResponseMessage>> _queue = new();

    public List<Uri> Requests { get; } = [];

    public FakeHandler Route(string url, Func<HttpResponseMessage> response)
    {
        _routes[url] = response;
        return this;
    }

    public FakeHandler Enqueue(Func<HttpResponseMessage> response)
    {
        _queue.Enqueue(response);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Uri uri = request.RequestUri!;
        Requests.Add(uri);
        if (_routes.TryGetValue(uri.AbsoluteUri, out var route))
        {
            return Task.FromResult(route());
        }

        return Task.FromResult(_queue.Count > 0 ? _queue.Dequeue()() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public static HttpResponseMessage Bytes(byte[] data, string contentType, bool declareLength = true)
    {
        var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
        res.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        if (!declareLength)
        {
            res.Content.Headers.ContentLength = null;
        }

        return res;
    }

    public static HttpResponseMessage Redirect(string location)
    {
        var res = new HttpResponseMessage(HttpStatusCode.Found);
        res.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return res;
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
