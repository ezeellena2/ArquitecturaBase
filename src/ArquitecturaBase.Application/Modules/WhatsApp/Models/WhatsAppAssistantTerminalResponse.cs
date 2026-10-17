namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

internal sealed record WhatsAppAssistantTerminalResponse(Guid ProposalId, string? Body, string Culture)
{
    public override string ToString() => "WhatsAppAssistantTerminalResponse { Content = [REDACTED] }";
}
