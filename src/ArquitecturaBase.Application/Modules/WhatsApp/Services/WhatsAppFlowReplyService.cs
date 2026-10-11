using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed partial class WhatsAppFlowReplyService(IWhatsAppFlowSessionReader sessions,
    IWhatsAppFlowReplyProtector replyProtector, IWhatsAppFlowSessionProtector sessionProtector,
    IWhatsAppFlowProtector flowProtector, IStructuredRecordSink records,
    IWhatsAppFlowReplyStateService stateService,
    ILogger<WhatsAppFlowReplyService> logger) : IWhatsAppFlowReplyService
{
    private const int BatchSize = 50;

    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        OperationLog.Handling(logger, "ProcessWhatsAppFlowReplies");
        var pending = await sessions.ListPendingFlowRepliesAsync(BatchSize, cancellationToken);
        foreach (var message in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ProcessOneAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogReplyProcessingFailed(logger, exception.GetType().Name);
            }
        }
        OperationLog.Handled(logger, "ProcessWhatsAppFlowReplies");
    }

    private async Task ProcessOneAsync(WhatsAppPendingFlowReplyRow message, CancellationToken cancellationToken)
    {
        string json;
        try { json = replyProtector.Read(message.WaMessageId, message.ContactId, message.ProtectedResponse); }
        catch (Exception exception) when (exception is CryptographicException or DecoderFallbackException or InvalidOperationException)
        {
            await stateService.MarkProcessedAsync(message.WaMessageId, null, WhatsAppFlowCaptureState.Unrecognized, null, null, null, cancellationToken);
            return;
        }
        if (!TryReadResponse(json, out var token, out var answers))
        {
            await stateService.MarkProcessedAsync(message.WaMessageId, null, WhatsAppFlowCaptureState.Rejected, null, null,
                FlowErrors.InvalidValue.Code, cancellationToken);
            return;
        }

        var tokenBytes = Encoding.UTF8.GetBytes(token);
        string tokenHash;
        try { tokenHash = Convert.ToHexStringLower(SHA256.HashData(tokenBytes)); }
        finally { CryptographicOperations.ZeroMemory(tokenBytes); }

        var session = await sessions.FindReplySessionAsync(message.ContactId, tokenHash, cancellationToken);
        if (session is null)
        {
            await stateService.MarkProcessedAsync(message.WaMessageId, null, WhatsAppFlowCaptureState.Unrecognized, null, null, null, cancellationToken);
            return;
        }
        if (!session.IsCurrentActiveRecipient || message.OccurredAtUtc >= session.ExpiresAtUtc)
        {
            await stateService.MarkProcessedAsync(message.WaMessageId, session.SessionId, WhatsAppFlowCaptureState.Unrecognized, null, null, null, cancellationToken);
            return;
        }

        if (session.CaptureState is WhatsAppFlowCaptureState.Rejected or WhatsAppFlowCaptureState.Unrecognized)
        {
            await stateService.MarkProcessedAsync(message.WaMessageId, session.SessionId, session.CaptureState, null, null,
                session.CaptureErrorCode ?? FlowErrors.InvalidValue.Code, cancellationToken);
            return;
        }

        WhatsAppFlowSessionPayload payload;
        WhatsAppFlowProjection projection;
        try
        {
            payload = sessionProtector.Read(session.SessionId, session.ContactId, session.UserId,
                session.VersionId, session.ProtectedPayload);
            projection = flowProtector.ReadProjection(session.OperationId, session.BindingId, session.VersionId,
                session.Culture, session.ProtectedProjection);
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidOperationException or JsonException)
        {
            await stateService.MarkProcessedAsync(message.WaMessageId, session.SessionId, WhatsAppFlowCaptureState.Unrecognized, null, null, null, cancellationToken);
            return;
        }
        if (payload.Format != 1 || payload.ContactId != session.ContactId || payload.UserId != session.UserId
            || payload.DefinitionId != session.DefinitionId || payload.VersionId != session.VersionId
            || payload.BindingId != session.BindingId || payload.Culture != session.Culture || payload.Token != token
            || payload.ExpectedHash != projection.Hash || projection.CodecVersion != WhatsAppFlowCompiler.CodecVersion
            || !ProjectionIsWellFormed(projection))
        {
            await stateService.MarkProcessedAsync(message.WaMessageId, session.SessionId, WhatsAppFlowCaptureState.Unrecognized, null, null, null, cancellationToken);
            return;
        }

        // BusinessForms owns its own UoW. If this process dies before the WhatsApp marker below, the same session
        // receipt makes the next attempt return the original record without allocating another number.
        var decoded = DecodeAnswers(projection, answers);
        if (decoded.IsFailure)
        {
            await stateService.MarkProcessedAsync(message.WaMessageId, session.SessionId, WhatsAppFlowCaptureState.Rejected,
                null, null, decoded.Error.Code, cancellationToken);
            return;
        }

        var captured = await records.CaptureAsync(new StructuredRecordCaptureRequest(session.SessionId,
            session.DefinitionId, session.VersionId, session.UserId, message.OccurredAtUtc, decoded.Value), cancellationToken);
        if (captured.State is "Unavailable") return;
        var state = captured.State == "Completed" ? WhatsAppFlowCaptureState.Completed : WhatsAppFlowCaptureState.Rejected;
        var displayNumber = captured.State == "Completed" && captured.Number is { } number
            ? session.Prefix + "-" + number.ToString("D6", CultureInfo.InvariantCulture) : null;
        await stateService.MarkProcessedAsync(message.WaMessageId, session.SessionId, state, captured.RecordId, displayNumber, captured.ErrorCode, cancellationToken);
    }

    private static bool TryReadResponse(string json, out string token, out IReadOnlyDictionary<string, string> answers)
    {
        token = "";
        answers = new Dictionary<string, string>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("flow_token", out var tokenValue)
                || tokenValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tokenValue.GetString())) return false;
            token = tokenValue.GetString()!;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == "flow_token") continue;
                if (!property.Name.StartsWith("field_", StringComparison.Ordinal)
                    || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)) return false;
                values.Add(property.Name, property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()! : property.Value.GetRawText());
            }
            answers = values;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException) { return false; }
    }

    private static bool ProjectionIsWellFormed(WhatsAppFlowProjection projection)
    {
        if (projection.Format != 1 || projection.Mapping.Count > 40 || projection.Culture is not ("es" or "en")) return false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var fieldIds = new HashSet<Guid>();
        return projection.Mapping.All(field => field.FieldId != Guid.Empty
            && field.Key == "field_" + field.FieldId.ToString("N")
            && keys.Add(field.Key) && fieldIds.Add(field.FieldId)
            && field.Type is "shortText" or "longText" or "decimal" or "date" or "boolean" or "choice");
    }

    private static Result<Dictionary<string, string>> DecodeAnswers(WhatsAppFlowProjection projection,
        IReadOnlyDictionary<string, string> answers)
    {
        var decoded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in projection.Mapping)
        {
            answers.TryGetValue(field.Key, out var raw);
            var value = WhatsAppFlowValueCodec.Decode(new WhatsAppFlowFieldMapping(field.FieldId, field.Key,
                field.Type, field.Required, field.MaxLength, field.Choices), raw);
            if (value.IsFailure)
                return FlowErrors.InvalidValue;
            if (!value.Value.IsPresent) continue;
            var canonical = value.Value.Text
                ?? value.Value.Decimal
                ?? value.Value.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                ?? (value.Value.Boolean is { } boolean ? boolean ? "true" : "false" : null)
                ?? value.Value.ChoiceId?.ToString("D");
            if (canonical is not null) decoded[field.Key] = canonical;
        }

        foreach (var answer in answers)
            if (!projection.Mapping.Any(field => field.Key == answer.Key)) decoded[answer.Key] = answer.Value;
        return decoded;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A WhatsApp Flow reply could not be processed; it remains pending for another pass ({ExceptionType}).")]
    private static partial void LogReplyProcessingFailed(ILogger logger, string exceptionType);
}
