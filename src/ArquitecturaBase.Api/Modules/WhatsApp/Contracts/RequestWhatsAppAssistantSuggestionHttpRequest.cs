namespace ArquitecturaBase.Api.Modules.WhatsApp.Contracts;

public sealed record RequestWhatsAppAssistantSuggestionHttpRequest(Guid RequestId, Guid SourceMessageId, long ExpectedModeRevision)
{
    public override string ToString() => "RequestWhatsAppAssistantSuggestionHttpRequest { Context = [REDACTED] }";
}
