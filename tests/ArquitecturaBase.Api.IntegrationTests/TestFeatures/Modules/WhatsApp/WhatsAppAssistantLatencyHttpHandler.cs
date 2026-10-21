using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

// In-process protocol samples only: no external provider, model quality or audio codec is qualified.
internal sealed class WhatsAppAssistantLatencyHttpHandler(WhatsAppAssistantLatencyHttpTrace trace) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var endpoint = request.RequestUri ?? throw new InvalidOperationException("Missing latency provider endpoint.");
        Require(request.Method == HttpMethod.Post && endpoint is
            { Scheme: "https", Host: "api.openai.com", IsDefaultPort: true }, "Unexpected latency provider endpoint.");
        if (endpoint.AbsolutePath == "/v1/audio/transcriptions")
        {
            trace.StartAudio();
            Require(request.Content is MultipartFormDataContent, "Expected audio multipart content.");
            var parts = ((MultipartFormDataContent)request.Content!).ToDictionary(
                part => part.Headers.ContentDisposition?.Name?.Trim('"')
                    ?? throw new InvalidOperationException("Missing audio multipart name."), StringComparer.Ordinal);
            Require(parts.Count == 3 && parts.ContainsKey("file") && parts.ContainsKey("model")
                && parts.ContainsKey("response_format"), "Unexpected audio multipart fields.");
            Require((await parts["file"].ReadAsByteArrayAsync(cancellationToken)).Length > 0,
                "Missing latency audio bytes.");
            Require(!string.IsNullOrWhiteSpace(await parts["model"].ReadAsStringAsync(cancellationToken))
                && await parts["response_format"].ReadAsStringAsync(cancellationToken) == "json",
                "Unexpected audio transcription format.");
            var audio = JsonResponse(JsonSerializer.Serialize(new
            {
                text = "Visité Taller Central.", languages = new[] { new { code = "es" } },
                usage = new { type = "duration", seconds = 1.0m },
            }));
            trace.AudioReturned();
            return audio;
        }
        Require(endpoint.AbsolutePath == "/v1/responses", "Unexpected latency text endpoint.");
        var ordinal = trace.StartText();
        Require(request.Content is not null, "Missing latency text request.");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var root = body.RootElement;
        Require(!root.GetProperty("store").GetBoolean() && !root.GetProperty("parallel_tool_calls").GetBoolean(),
            "Expected private serial tool execution.");
        _ = RequiredText(root, "model");
        var input = root.GetProperty("input");
        Require(input.ValueKind == JsonValueKind.Array, "Missing latency conversation input.");
        var results = input.EnumerateArray().Where(item => OptionalText(item, "type") == "function_call_output").ToArray();
        string name;
        string arguments;
        var terminal = false;
        if (results.Length == 0)
        {
            name = "forms_assistant-candidates";
            arguments = "{}";
        }
        else
        {
            var last = results[^1];
            var callId = RequiredText(last, "call_id");
            var call = input.EnumerateArray().Single(item => OptionalText(item, "type") == "function_call"
                && RequiredText(item, "call_id") == callId);
            using var observed = JsonDocument.Parse(RequiredText(last, "output"));
            var value = observed.RootElement;
            switch (RequiredText(call, "name"))
            {
                case "forms_assistant-candidates":
                    Require(RequiredText(value, "state") == "complete", "Expected a complete real candidate read.");
                    Require(input.EnumerateArray().Any(item => OptionalText(item, "role") == "user"
                        && RequiredText(item, "content").Contains("Taller Central", StringComparison.Ordinal)),
                        "The latency input must contain the proposed business fact.");
                    var form = value.GetProperty("payload").GetProperty("forms").EnumerateArray().Single();
                    var field = form.GetProperty("questions").EnumerateArray()
                        .Single(item => RequiredText(item, "type") == "shortText");
                    Require(field.GetProperty("maxLength").GetInt32() >= "Taller Central".Length,
                        "Observed text limit cannot hold the latency fact.");
                    name = "forms_propose-registration";
                    arguments = JsonSerializer.Serialize(new
                    {
                        definitionId = RequiredGuid(form, "definitionId"), versionId = RequiredGuid(form, "versionId"),
                        values = new[] { new { fieldId = RequiredGuid(field, "fieldId"), type = "shortText", value = "Taller Central" } },
                        reason = "La persona describió una visita a Taller Central.",
                    });
                    break;
                case "forms_propose-registration":
                    Require(RequiredText(value, "state") == "proposed", "Expected a real observed proposal.");
                    name = "assistant_proposal";
                    arguments = JsonSerializer.Serialize(new { proposalId = RequiredGuid(value, "proposalId") });
                    terminal = true;
                    break;
                default:
                    throw new InvalidOperationException("Unexpected latency tool result.");
            }
        }
        Require(root.GetProperty("tools").EnumerateArray().Any(tool => RequiredText(tool, "name") == name),
            "The latency tool was not offered by the real coordinator.");
        var id = "latency-call-" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var response = JsonResponse(JsonSerializer.Serialize(new
        {
            status = "completed", output = new[]
            {
                new { type = "function_call", id = "item-" + id, call_id = id, name, arguments, status = "completed" },
            },
            usage = new { input_tokens = 10, output_tokens = 2 },
        }));
        trace.TextReturned(terminal);
        return response;
    }

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static string? OptionalText(JsonElement value, string property) => value.TryGetProperty(property, out var text)
        ? text.GetString() : null;
    private static string RequiredText(JsonElement value, string property) => OptionalText(value, property) is { } text
        && !string.IsNullOrWhiteSpace(text) ? text : throw new InvalidOperationException("Missing latency protocol text.");
    private static Guid RequiredGuid(JsonElement value, string property)
    {
        var id = value.GetProperty(property).GetGuid();
        Require(id != Guid.Empty, "Missing observed latency identity.");
        return id;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class WhatsAppAssistantLatencyHttpTrace
{
    private int _textCalls;
    private int _audioCalls;
    private long _audioReturnedTimestamp;
    private long _textTerminalReturnedTimestamp;
    public int TextCalls => Volatile.Read(ref _textCalls);
    public int AudioCalls => Volatile.Read(ref _audioCalls);
    public long? AudioReturnedTimestamp => Timestamp(ref _audioReturnedTimestamp);
    public long? TextTerminalReturnedTimestamp => Timestamp(ref _textTerminalReturnedTimestamp);
    public ConcurrentQueue<long> TextReturnedTimestamps { get; } = new();
    internal int StartText() => Interlocked.Increment(ref _textCalls);
    internal void StartAudio() => Interlocked.Increment(ref _audioCalls);
    internal void AudioReturned() => Interlocked.Exchange(ref _audioReturnedTimestamp, Stopwatch.GetTimestamp());
    internal void TextReturned(bool terminal)
    {
        var timestamp = Stopwatch.GetTimestamp();
        TextReturnedTimestamps.Enqueue(timestamp);
        if (terminal) Interlocked.Exchange(ref _textTerminalReturnedTimestamp, timestamp);
    }
    private static long? Timestamp(ref long field)
    {
        var value = Volatile.Read(ref field);
        return value == 0 ? null : value;
    }
}
