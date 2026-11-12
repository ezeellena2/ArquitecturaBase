using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// Task 8 of the documents: a PDF or a photo that is used is read by the engine (ReadDocument) in the linked person's own automatic context,
// like an audio's normalization: Actor and Subject are who sent it, without capabilities, terminals or history; the request is checked
// again on every poll and before its reading is kept. The document's content lasts as long as its message's body.
internal sealed class WhatsAppReceivedDocumentReadingPolicy(IUserReader users, IWhatsAppFormRecipientReader recipients, ISystemSettingsReader settings,
    WhatsAppContentRetention retention, TimeProvider clock)
{
    public const string ChannelId = "whatsapp-assistant";

    /// <summary>How a reading is kept with its message: the web defaults with the enums by name, as the engine keeps it.</summary>
    public static JsonSerializerOptions ReadingJson { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public DateTime NowUtc => clock.GetUtcNow().UtcDateTime;

    public DateTime ExpiresAtUtc(WhatsAppReceivedDocumentSourceRow source) => source.OccurredAtUtc.AddDays(retention.MessageRetentionDays);

    public bool IsExpired(WhatsAppReceivedDocumentSourceRow source) => ExpiresAtUtc(source) <= NowUtc;

    public static string SerializeReading(StructuredDocumentReading reading) => JsonSerializer.Serialize(reading, ReadingJson);

    /// <summary>The account of the linked person, active, with the chat's number confirmed; the conversation's language and zone.</summary>
    public async Task<Result<AssistantChannelContext>> CaptureAsync(WhatsAppReceivedDocumentSourceRow source, CancellationToken ct)
    {
        if (source.Mode != WhatsAppConversationMode.Assistant || source.LinkedUserId is not { } subject) return WhatsAppChatErrors.RequestConflict;
        var recipient = await recipients.FindAsync(source.ContactId, ct);
        if (recipient is not { IsActive: true, WaId: not null } || recipient.UserId != subject || recipient.LinkRevision != source.LinkRevision
            || await users.FindByIdAsync(subject, ct) is not { IsActive: true, PhoneNumberConfirmed: true } account || account.PhoneNumber != "+" + recipient.WaId)
            return WhatsAppChatErrors.Forbidden;
        var presentation = await settings.FindPresentationAsync(ct);
        var culture = recipient.Culture is "es" or "en" ? recipient.Culture : presentation.DefaultCulture;
        if (culture is not ("es" or "en") || !TimeZoneInfo.TryFindSystemTimeZoneById(presentation.DefaultTimeZoneId, out _)) return WhatsAppChatErrors.RequestConflict;
        return new AssistantChannelContext(ChannelId, subject, subject, culture, presentation.DefaultTimeZoneId, new([], []), [], [])
        { InteractionMode = AssistantInteractionMode.Assistant };
    }

    /// <summary>
    /// The context sealed with the request, while the link it was fixed with lasts and the same person still may: a relink, another
    /// account or a context that is not this channel's is a conflict.
    /// </summary>
    public async Task<Result<AssistantChannelContext>> RevalidateAsync(WhatsAppReceivedDocumentSourceRow source, CancellationToken ct)
    {
        if (source.CapturedLinkRevision != source.LinkRevision || source.ActorUserId is not { } actor || source.ContextJson is null)
            return WhatsAppChatErrors.RequestConflict;
        AssistantChannelContext? captured;
        try { captured = JsonSerializer.Deserialize<AssistantChannelContext>(source.ContextJson, JsonSerializerOptions.Web); }
        catch (JsonException) { return WhatsAppChatErrors.RequestConflict; }
        if (captured is null || captured.ActorUserId != actor || captured.SubjectUserId != actor || captured.ChannelId != ChannelId
            || captured.InteractionMode != AssistantInteractionMode.Assistant || captured.AllowedCapabilities is not { Count: 0 }
            || captured.AllowedTerminals is not { Count: 0 } || captured.RequiredPermissions?.Actor is not { Count: 0 }
            || captured.RequiredPermissions.Subject is not { Count: 0 }) return WhatsAppChatErrors.RequestConflict;
        var current = await CaptureAsync(source, ct);
        return current.IsFailure ? current.Error : current.Value.ActorUserId == actor ? captured : WhatsAppChatErrors.RequestConflict;
    }

    /// <summary>
    /// The engine reads the private copy alone: its key, the person's file name, its type, size and SHA-256, its pages (a photo is one) and
    /// the expiry of the message's content. Null for a copy that is not a PDF or a photo.
    /// </summary>
    public static AssistantOperationRequest? Operation(Guid requestId, AssistantChannelContext context, string storageKey, string name, long size,
        string sha256, int? pages, DateTime occurredAtUtc, DateTime expiresAtUtc)
    {
        if (!WhatsAppReceivedDocumentPreparationPolicy.TryParseKey(storageKey, out _, out var kind) || kind == StructuredDocumentKind.Docx) return null;
        return new AssistantOperationRequest(requestId, context, "", occurredAtUtc, expiresAtUtc)
        {
            Document = new(storageKey, name, WhatsAppReceivedDocumentPreparationPolicy.MediaType(kind), size, sha256,
                kind == StructuredDocumentKind.Pdf ? pages ?? 1 : 1, expiresAtUtc),
        };
    }

    /// <summary>
    /// The day's reading quota: the engine refuses the request with too many requests and does not say it may pass, as it does with its
    /// momentary capacity.
    /// </summary>
    public static bool IsQuota<T>(Result<T> outcome) => outcome.IsFailure && outcome.Error.Type == ErrorType.TooManyRequests && !IsRetryable(outcome.Error);

    public static bool IsRetryable(Error error) => error.Metadata?.TryGetValue("retryable", out var retryable) == true && retryable is true;
}
