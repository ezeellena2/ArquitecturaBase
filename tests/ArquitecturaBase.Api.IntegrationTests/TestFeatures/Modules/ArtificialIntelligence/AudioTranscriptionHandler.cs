using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

// Fictitious in-process HTTP only. Bytes are protocol samples, not a qualified audio recording.
internal sealed class AudioTranscriptionHandler(
    Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<string> Urls { get; } = [];
    public List<string?> Credentials { get; } = [];
    public List<string?> AuthenticationSchemes { get; } = [];
    public List<IReadOnlyDictionary<string, string>> Fields { get; } = [];
    public List<byte[]> Files { get; } = [];
    public List<string?> FileNames { get; } = [];
    public List<string?> ContentTypes { get; } = [];

    public static AudioTranscriptionHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK,
        string contentType = "application/json", bool chunked = false) => new(_ =>
    {
        HttpContent content = chunked
            ? new StreamContent(new NonSeekingStream(Encoding.UTF8.GetBytes(body)))
            : new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return Task.FromResult(new HttpResponseMessage(status) { Content = content });
    });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Assert.Equal(HttpMethod.Post, request.Method);
        Urls.Add(request.RequestUri!.AbsoluteUri);
        AuthenticationSchemes.Add(request.Headers.Authorization?.Scheme);
        Credentials.Add(request.Headers.Authorization?.Parameter);
        var form = Assert.IsType<MultipartFormDataContent>(request.Content);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in form)
        {
            var name = part.Headers.ContentDisposition!.Name!.Trim('"');
            if (name == "file")
            {
                Files.Add(await part.ReadAsByteArrayAsync(cancellationToken));
                FileNames.Add(part.Headers.ContentDisposition.FileName?.Trim('"'));
                ContentTypes.Add(part.Headers.ContentType?.ToString());
            }
            else
            {
                Assert.True(fields.TryAdd(name, await part.ReadAsStringAsync(cancellationToken)));
            }
        }
        Fields.Add(fields);
        Started.TrySetResult();
        return await respond(cancellationToken);
    }

    private sealed class NonSeekingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}

internal sealed class WaitingAudioReadStream : Stream
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
}
