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

    /// <summary>Content with a stream the test controls and a chosen Content-Length.</summary>
    public sealed class StreamContent2(Func<Stream> open, long? length) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            open().CopyToAsync(stream);

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(open());

        protected override bool TryComputeLength(out long l)
        {
            l = length ?? 0;
            return length is not null;
        }

        public static HttpResponseMessage Response(Func<Stream> open, long? length, string contentType)
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent2(open, length) };
            res.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return res;
        }
    }

    /// <summary>Blocks on read until cancelled.</summary>
    public sealed class BlockingStream : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Throws IOException on read.</summary>
    public sealed class ThrowingStream : Stream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            throw new IOException("connection dropped");

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection dropped");
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
