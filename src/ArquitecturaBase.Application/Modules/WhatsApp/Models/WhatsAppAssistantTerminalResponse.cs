using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

internal sealed record WhatsAppAssistantTerminalResponse(Guid ProposalId, string? Body, string Culture)
{
    /// <summary>An authoring summary's «Crear borrador» or «Crear catálogo» and «Cambiar algo» (IA3-A); null for a fixed text.</summary>
    public IReadOnlyList<WhatsAppOutgoingQuickReply>? Buttons { get; init; }
    public override string ToString() => "WhatsAppAssistantTerminalResponse { Content = [REDACTED] }";
}
