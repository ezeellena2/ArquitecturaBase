using System.Text.Json;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed partial class WhatsAppMessage
{
    public DateTime? AssistantProcessedAtUtc { get; private set; }
    public string? QuickReplyTitlesJson { get; private set; }

    public void AttributeAssistant(IReadOnlyList<string>? quickReplyTitles = null)
    {
        if (Direction != WhatsAppMessageDirection.Outbound || OperatorUserId.HasValue || Origin == WhatsAppMessageOrigin.FormAction)
            throw new InvalidOperationException("Only an outbound system message can belong to the assistant.");
        var titles = quickReplyTitles?.ToArray() ?? [];
        if (titles.Length > 3 || titles.Any(title=>title.Length > 20 || !WhatsAppOutgoingMessage.IsVisibleBody(title)))
            throw new ArgumentException("The stored reply titles must match the provider limits.", nameof(quickReplyTitles));
        Origin = WhatsAppMessageOrigin.Assistant;
        QuickReplyTitlesJson = titles.Length == 0 ? null : JsonSerializer.Serialize(titles);
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
