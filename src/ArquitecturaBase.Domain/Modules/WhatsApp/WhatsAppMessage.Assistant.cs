using System.Text.Json;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed partial class WhatsAppMessage
{
    public DateTime? AssistantProcessedAtUtc { get; private set; }
    public string? QuickReplyTitlesJson { get; private set; }

    /// <summary>Casos del asistente: the why of the reply that went out, copied from its outgoing; it goes away with the text.</summary>
    public string? AssistantReplyJson { get; private set; }

    /// <summary>
    /// The assistant's outbound message, with the titles of its options: buttons (up to three, of up to 20 characters) or, for a list
    /// (<paramref name="list"/>, task 9 of the documents), its rows (up to ten, of up to 24).
    /// </summary>
    public void AttributeAssistant(IReadOnlyList<string>? quickReplyTitles = null, string? replyJson = null, bool list = false)
    {
        if (replyJson is { Length: > WhatsAppOutgoingMessage.MaxAssistantReplyJsonLength })
            throw new ArgumentException("The why of a reply is bounded.", nameof(replyJson));
        if (Direction != WhatsAppMessageDirection.Outbound || OperatorUserId.HasValue || Origin == WhatsAppMessageOrigin.FormAction)
            throw new InvalidOperationException("Only an outbound system message can belong to the assistant.");
        var titles = quickReplyTitles?.ToArray() ?? [];
        var (count, length) = list ? (WhatsAppOutgoingMessage.MaxListRows, WhatsAppOutgoingMessage.MaxListRowTitleLength) : (3, 20);
        if (titles.Length > count || titles.Any(title=>title.Length > length || !WhatsAppOutgoingMessage.IsVisibleBody(title)))
            throw new ArgumentException("The stored reply titles must match the provider limits.", nameof(quickReplyTitles));
        Origin = WhatsAppMessageOrigin.Assistant;
        QuickReplyTitlesJson = titles.Length == 0 ? null : JsonSerializer.Serialize(titles);
        AssistantReplyJson = replyJson;
        Classify(WhatsAppMessagePurpose.Business);
    }

    public bool MarkAssistantProcessed(DateTime nowUtc)
    {
        if (Direction != WhatsAppMessageDirection.Inbound)
            throw new InvalidOperationException("Only an inbound message can record interpretation processing.");
        if (AssistantProcessedAtUtc.HasValue) return false;
        AssistantProcessedAtUtc = nowUtc;
        return true;
    }
}
