using System.Buffers;
using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;

/// <summary>
/// One bounded POST to the Responses API with the document inline: a PDF as an <c>input_file</c>, a photo as an <c>input_image</c> with
/// high detail, and the reading as a strict JSON schema, without storing it. Quotas, profile, secret policy and durable fences belong
/// to the engine; this client never goes through the conversation's 128 KiB body.
/// </summary>
internal sealed class OpenAiDocumentReadingClient(HttpClient client, AiAssistantLimits limits, TimeProvider timeProvider) : ILlmDocumentReadingClient
{
    internal const int MaximumDocumentBytes = StructuredAttachmentLimits.MaximumBytes;
    // The document in base64 plus 64 KiB for the instructions, the schema and the envelope.
    internal const int MaximumRequestBytes = (MaximumDocumentBytes + 2) / 3 * 4 + 64 * 1024;
    // The name is not the person's: what the provider reads is the content.
    internal const string FileName = "document.pdf";
    private const string Pdf = "application/pdf";
    private static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");
    // A base64 data URL travels as is: the default encoder would escape every «+» and outgrow the body's cap.
    private static readonly JsonWriterOptions Writer = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    // Whole base64 groups: every piece but the last encodes without padding.
    private const int Base64Piece = 3 * 1024;
    private static readonly string[] Fields = ["language", "problem", "readability", "text", "truncated"];

    internal static object Format { get; } = new
    {
        type = "json_schema", name = "document_reading", strict = true,
        schema = new
        {
            type = "object",
            properties = new
            {
                readability = new { type = "string", @enum = new[] { "readable", "partial", "unreadable" } },
                problem = new { type = new[] { "string", "null" }, @enum = new string?[] { "blurry", "dark", "cut", "covered", "other", null } },
                language = new { type = new[] { "string", "null" } },
                text = new { type = "string" },
                truncated = new { type = "boolean" },
            },
            required = new[] { "readability", "problem", "language", "text", "truncated" },
            additionalProperties = false,
        },
    };

    public async Task<Result<LlmDocumentReadingResponse>> ReadAsync(LlmDocumentReadingRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return AiAssistantErrors.Canceled;
        if (request.Provider != AiProvider.OpenAI || string.IsNullOrWhiteSpace(request.Model) || request.Model.Length > 128
            || request.Model.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            || string.IsNullOrWhiteSpace(request.ApiKey) || request.ApiKey.Any(char.IsControl) || string.IsNullOrWhiteSpace(request.Instructions)
            || request.ContentType is not (Pdf or "image/jpeg" or "image/png"))
            return AiAssistantErrors.Invalid;
        if (request.Content.Length is <= 0 or > MaximumDocumentBytes
            || request.MaxOutputTokens <= 0 || request.MaxOutputTokens > limits.DocumentOutputTokens
            || request.MaxResponseBytes <= 0 || request.MaxResponseBytes > limits.ProviderResponseBytes
            || request.TimeoutSeconds <= 0 || request.TimeoutSeconds > limits.DocumentTimeoutSeconds)
            return AiAssistantErrors.LimitExceeded;
        // Declared before the message: it holds the document and goes back to the pool, cleared, only after the message is disposed.
        using var body = new PooledBody((request.Content.Length + 2) / 3 * 4 + 64 * 1024);
        WriteBody(request, body);
        if (body.WrittenCount > MaximumRequestBytes) return AiAssistantErrors.LimitExceeded;

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        message.Content = new ReadOnlyMemoryContent(body.WrittenMemory);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        try
        {
            // The engine must have committed BeginProviderStep before entering this call.
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            // Do not parse or log provider error bodies. No retry after a possible POST.
            if ((int)response.StatusCode is >= 400 and < 500) return AiAssistantErrors.ProviderRejected;
            if (!response.IsSuccessStatusCode) return (int)response.StatusCode >= 500
                ? AiAssistantErrors.ProviderUncertain : AiAssistantErrors.ProviderRejected;
            var responseType = response.Content.Headers.ContentType?.MediaType;
            if (responseType is null || !(responseType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || responseType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
                return AiAssistantErrors.InvalidOutput;
            if (response.Content.Headers.ContentLength > request.MaxResponseBytes) return AiAssistantErrors.LimitExceeded;
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            using var received = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, linked.Token)) > 0)
            {
                if (received.Length + read > request.MaxResponseBytes) return AiAssistantErrors.LimitExceeded;
                received.Write(buffer, 0, read);
            }
            using var document = JsonDocument.Parse(received.GetBuffer().AsMemory(0, checked((int)received.Length)),
                new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !LlmConversationHttpTransport.HasUniqueProperties(document.RootElement))
                return AiAssistantErrors.InvalidOutput;
            return Parse(document.RootElement);
        }
        catch (OperationCanceledException) { return AiAssistantErrors.ProviderUncertain; }
        catch (HttpRequestException) { return AiAssistantErrors.ProviderUncertain; }
        catch (IOException) { return AiAssistantErrors.ProviderUncertain; }
        catch (JsonException) { return AiAssistantErrors.InvalidOutput; }
    }

    // The whole request in one pass and in this order, with the relaxed encoder: the file is never a base64 or UTF-16 string, only
    // pieces of the data URL written straight into the body.
    private static void WriteBody(LlmDocumentReadingRequest request, IBufferWriter<byte> output)
    {
        using var writer = new Utf8JsonWriter(output, Writer);
        writer.WriteStartObject();
        writer.WriteString("model", request.Model);
        writer.WriteString("instructions", request.Instructions);
        writer.WriteStartArray("input");
        writer.WriteStartObject();
        writer.WriteString("role", "user");
        writer.WriteStartArray("content");
        writer.WriteStartObject();
        if (request.ContentType == Pdf)
        {
            writer.WriteString("type", "input_file");
            writer.WriteString("filename", FileName);
            writer.WritePropertyName("file_data");
            WriteDataUrl(writer, request.ContentType, request.Content.Span);
        }
        else
        {
            writer.WriteString("type", "input_image");
            writer.WritePropertyName("image_url");
            WriteDataUrl(writer, request.ContentType, request.Content.Span);
            writer.WriteString("detail", "high");
        }
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteStartObject("text");
        writer.WritePropertyName("format");
        JsonSerializer.Serialize(writer, Format);
        writer.WriteEndObject();
        writer.WriteNumber("max_output_tokens", request.MaxOutputTokens);
        writer.WriteBoolean("store", false);
        if (request.LowReasoningEffort)
        {
            writer.WriteStartObject("reasoning");
            writer.WriteString("effort", "low");
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.Flush();
    }

    // One JSON string in UTF-8 segments: the prefix, then the base64 of 3 KiB of the file at a time (whole groups, padded only at the end).
    private static void WriteDataUrl(Utf8JsonWriter writer, string contentType, ReadOnlySpan<byte> content)
    {
        writer.WriteStringValueSegment(Encoding.UTF8.GetBytes($"data:{contentType};base64,"), isFinalSegment: false);
        Span<byte> encoded = stackalloc byte[Base64Piece / 3 * 4];
        while (!content.IsEmpty)
        {
            var piece = content[..Math.Min(content.Length, Base64Piece)];
            Base64.EncodeToUtf8(piece, encoded, out _, out var written);
            writer.WriteStringValueSegment(encoded[..written], isFinalSegment: false);
            content = content[piece.Length..];
        }
        writer.WriteStringValueSegment(ReadOnlySpan<byte>.Empty, isFinalSegment: true);
    }

    // A refusal and an incomplete answer keep the usage they report, without a reading; anything that is not the schema is invalid.
    private static Result<LlmDocumentReadingResponse> Parse(JsonElement root)
    {
        var usage = LlmConversationHttpTransport.Usage(root);
        var status = LlmConversationHttpTransport.Text(root, "status");
        if (status == "incomplete") return new LlmDocumentReadingResponse("incomplete", null, usage);
        if (status == "failed") return new LlmDocumentReadingResponse("refusal", null, usage);
        if (status != "completed" || !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return AiAssistantErrors.InvalidOutput;
        string? text = null;
        foreach (var item in output.EnumerateArray())
        {
            var type = LlmConversationHttpTransport.Text(item, "type");
            if (type == "reasoning") continue;
            if (type != "message" || !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                return AiAssistantErrors.InvalidOutput;
            foreach (var block in content.EnumerateArray())
            {
                var blockType = LlmConversationHttpTransport.Text(block, "type");
                if (blockType == "refusal") return new LlmDocumentReadingResponse("refusal", null, usage);
                if (blockType != "output_text" || text is not null) return AiAssistantErrors.InvalidOutput;
                text = LlmConversationHttpTransport.Text(block, "text") ?? throw new JsonException("Missing output text.");
            }
        }
        return text is not null && Reading(text) is { } reading
            ? new LlmDocumentReadingResponse(LlmDocumentReadingResponse.Read, reading, usage) : AiAssistantErrors.InvalidOutput;
    }

    // Exactly the five fields with their types and values, as the model wrote them: the engine normalizes the language before validating.
    private static StructuredDocumentReading? Reading(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !LlmConversationHttpTransport.HasUniqueProperties(root)
            || !root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(Fields))
            return null;
        StructuredDocumentReadability? readability = LlmConversationHttpTransport.Text(root, "readability") switch
        {
            "readable" => StructuredDocumentReadability.Readable,
            "partial" => StructuredDocumentReadability.Partial,
            "unreadable" => StructuredDocumentReadability.Unreadable,
            _ => null,
        };
        var problem = root.GetProperty("problem");
        var cause = Problem(LlmConversationHttpTransport.Text(root, "problem"));
        var language = root.GetProperty("language");
        var text = root.GetProperty("text");
        var truncated = root.GetProperty("truncated");
        if (readability is null || problem.ValueKind != JsonValueKind.Null && cause is null
            || language.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)
            || text.ValueKind != JsonValueKind.String || truncated.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return null;
        return new StructuredDocumentReading(readability.Value, cause, language.GetString(), text.GetString()!, truncated.GetBoolean());
    }

    private static StructuredDocumentProblem? Problem(string? value) => value switch
    {
        "blurry" => StructuredDocumentProblem.Blurry,
        "dark" => StructuredDocumentProblem.Dark,
        "cut" => StructuredDocumentProblem.Cut,
        "covered" => StructuredDocumentProblem.Covered,
        "other" => StructuredDocumentProblem.Other,
        _ => null,
    };

    // The body in one array of the shared pool, sized for the whole request so the writer never makes it grow. It holds the document:
    // every array goes back cleared.
    private sealed class PooledBody(int capacity) : IBufferWriter<byte>, IDisposable
    {
        private byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);

        public int WrittenCount { get; private set; }
        public ReadOnlyMemory<byte> WrittenMemory => buffer.AsMemory(0, WrittenCount);

        public void Advance(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > buffer.Length - WrittenCount) throw new InvalidOperationException("Cannot advance past the end of the buffer.");
            WrittenCount += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => buffer.AsMemory(Ensure(sizeHint));
        public Span<byte> GetSpan(int sizeHint = 0) => buffer.AsSpan(Ensure(sizeHint));

        public void Dispose()
        {
            var returned = buffer;
            buffer = [];
            WrittenCount = 0;
            if (returned.Length > 0) ArrayPool<byte>.Shared.Return(returned, clearArray: true);
        }

        private int Ensure(int sizeHint)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            if (buffer.Length - WrittenCount < Math.Max(sizeHint, 1))
            {
                var larger = ArrayPool<byte>.Shared.Rent(checked(buffer.Length + Math.Max(sizeHint, buffer.Length)));
                buffer.AsSpan(0, WrittenCount).CopyTo(larger);
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                buffer = larger;
            }
            return WrittenCount;
        }
    }
}
