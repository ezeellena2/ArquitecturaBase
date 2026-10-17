namespace ArquitecturaBase.Api.Modules.WhatsApp.Contracts;

public sealed record SendWhatsAppAssistantSuggestionHttpRequest(Guid RequestId, IReadOnlyList<Guid> IncludedFieldIds)
{
    public override string ToString() => "SendWhatsAppAssistantSuggestionHttpRequest { Selection = [REDACTED] }";
}
