using ArquitecturaBase.Application.Models.Assistant;
using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;

internal sealed class HmacAiAssistantRequestFingerprint(AiKeyRing ring) : IAiAssistantRequestFingerprint
{
    public string ActiveKeyId => ring.ActiveKeyId;
    public string Create(AssistantOperationRequest request, string keyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = request.Context;
        var key = ring.AssistantFingerprintKey(keyId);
        var values = new List<object?>
        {
            "assistant-operation-request-fingerprint:v1", request.RequestId,
            context.ChannelId, context.ActorUserId, context.SubjectUserId, context.Culture, context.TimeZoneId,
            context.RequiredPermissions.Actor.Order(StringComparer.Ordinal).ToArray(),
            context.RequiredPermissions.Subject.Order(StringComparer.Ordinal).ToArray(),
            context.AllowedCapabilities.OrderBy(reference => reference.Id, StringComparer.Ordinal).ThenBy(reference => reference.Version)
                .Select(reference => new object[] { reference.Id, reference.Version }).ToArray(),
            context.AllowedTerminals.OrderBy(reference => reference.Id, StringComparer.Ordinal).ThenBy(reference => reference.Version)
                .Select(reference => new object[] { reference.Id, reference.Version }).ToArray(),
            request.Text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal), request.OccurredAtUtc, request.ContentExpiresAtUtc
        };
        if (request.Audio is { } audio) values.Add(new object?[] { "normalize-audio:v1", audio.StorageKey, audio.Name, audio.ContentType, audio.Size, audio.Sha256, audio.ExpiresAtUtc });
        // A spreadsheet binds its private copy; what was prepared comes from those bytes. One a turn inherits is not part of the request.
        if (request.Attachment is { } attachment) values.Add(Attachment("attachment:v1", attachment));
        if (context.InteractionMode != AssistantInteractionMode.Unspecified)
            values.Add(new object?[] { "assistant-interaction-mode:v1", context.InteractionMode });
        if (context.ChannelConversationId is { } conversationId)
            values.Add(new object?[] { "assistant-channel-conversation:v1", conversationId });
        if (request.PreviousRequestId is { } previousRequestId)
            values.Add(new object?[] { "assistant-previous-operation:v1", previousRequestId });
        var payload = JsonSerializer.SerializeToUtf8Bytes(values);
        try { return Convert.ToBase64String(HMACSHA256.HashData(key, payload)); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(payload); }
    }

    public bool Matches(AssistantOperationRequest request, string keyId, string fingerprint)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Create(request, keyId)), Convert.FromBase64String(fingerprint)); }
        catch (FormatException) { return false; }
    }

    public string Create(Guid ownerUserId, SubmitAiAssistantTurnRequest request, string keyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = ring.AssistantFingerprintKey(keyId);
        var values = new List<object?> { ownerUserId, request.RequestId, request.ConversationId,
            request.ExpectedRevision, request.ExpectedConnectionRevision, request.NormalizedQuestion };
        // A dictated turn binds its normalization; a typed one keeps the original fingerprint.
        if (request.AudioRequestId is { } audioRequestId) values.Add(new object?[] { "home-audio:v1", audioRequestId });
        if (request.Attachment is { } attachment) values.Add(Attachment("home-attachment:v1", attachment));
        var payload = JsonSerializer.SerializeToUtf8Bytes(values);
        try { return Convert.ToBase64String(HMACSHA256.HashData(key, payload)); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(payload); }
    }

    public bool Matches(Guid ownerUserId, SubmitAiAssistantTurnRequest request, string keyId, string fingerprint)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Create(ownerUserId, request, keyId)), Convert.FromBase64String(fingerprint)); }
        catch (FormatException) { return false; }
    }

    private static object?[] Attachment(string version, AssistantAttachmentInput attachment) =>
        [version, attachment.StorageKey, attachment.Name, attachment.ContentType, attachment.Size, attachment.Sha256, attachment.ExpiresAtUtc];
}
