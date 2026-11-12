using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

// Fictitious in-process HTTP only: it keeps the exact JSON body of each reading, as bytes and as text, and answers what the test says.
// The bytes are protocol samples, not a qualified PDF or photo.
internal sealed class DocumentReadingHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<string> Urls { get; } = [];
    public List<string?> Credentials { get; } = [];
    public List<string?> AuthenticationSchemes { get; } = [];
    public List<string?> ContentTypes { get; } = [];
    public List<string> Bodies { get; } = [];
    public List<byte[]> RawBodies { get; } = [];

    public static DocumentReadingHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK,
        string contentType = "application/json", bool chunked = false) => new(_ =>
    {
        HttpContent content = chunked
            ? new StreamContent(new NonSeekingStream(Encoding.UTF8.GetBytes(body)))
            : new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return Task.FromResult(new HttpResponseMessage(status) { Content = content });
    });

    // A completed Responses API answer whose only output text is the reading the test gives, with reasoning before it.
    public static string Completed(object reading, object? usage = null) => JsonSerializer.Serialize(new
    {
        id = "resp-fictitious", status = "completed",
        output = new object[]
        {
            new { type = "reasoning", id = "reasoning-fictitious", summary = Array.Empty<object>() },
            new { type = "message", id = "message-fictitious", role = "assistant", status = "completed",
                content = new[] { new { type = "output_text", text = reading as string ?? JsonSerializer.Serialize(reading), annotations = Array.Empty<object>() } } },
        },
        usage = usage ?? new { input_tokens = 1200, output_tokens = 300, total_tokens = 1500 },
    });

    public static object Reading(string text = "| Monto | $ 1.000 |", string readability = "readable", string? problem = null,
        string? language = "es", bool truncated = false) => new { readability, problem, language, text, truncated };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Assert.Equal(HttpMethod.Post, request.Method);
        Urls.Add(request.RequestUri!.AbsoluteUri);
        AuthenticationSchemes.Add(request.Headers.Authorization?.Scheme);
        Credentials.Add(request.Headers.Authorization?.Parameter);
        ContentTypes.Add(request.Content?.Headers.ContentType?.MediaType);
        var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        RawBodies.Add(body);
        Bodies.Add(Encoding.UTF8.GetString(body));
        Started.TrySetResult();
        return await respond(cancellationToken);
    }

    private sealed class NonSeekingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}

// The transport double of the provider adapter: it keeps what it received and answers a fixed reading.
internal sealed class DocumentReadingClient : ILlmDocumentReadingClient
{
    public int Calls { get; private set; }
    public LlmDocumentReadingRequest? Last { get; private set; }
    public Result<LlmDocumentReadingResponse> Response { get; set; } = new LlmDocumentReadingResponse(LlmDocumentReadingResponse.Read,
        new StructuredDocumentReading(StructuredDocumentReadability.Readable, null, "es", "| Monto | $ 1.000 |", false), new(1200, 300));

    public Task<Result<LlmDocumentReadingResponse>> ReadAsync(LlmDocumentReadingRequest request, CancellationToken ct)
    {
        Calls++;
        Last = request;
        return Task.FromResult(Response);
    }
}
