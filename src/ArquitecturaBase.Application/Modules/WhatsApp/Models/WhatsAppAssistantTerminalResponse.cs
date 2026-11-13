using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

internal sealed record WhatsAppAssistantTerminalResponse(Guid ProposalId, string? Body, string Culture)
{
    /// <summary>An authoring summary's «Crear borrador» or «Crear catálogo» and «Cambiar algo» (IA3-A); null for a fixed text.</summary>
    public IReadOnlyList<WhatsAppOutgoingQuickReply>? Buttons { get; init; }
    /// <summary>A question about what to build (IA3-A): the next message answers it and continues the same operation.</summary>
    public bool AwaitsAnswer { get; init; }
    /// <summary>Task 9 of the documents: up to ten options behind a button, instead of buttons, with the label of that button.</summary>
    public IReadOnlyList<WhatsAppOutgoingQuickReply>? List { get; init; }
    public string? ListButton { get; init; }
    /// <summary>Task 9: the form prefilled with what a received document says, sent with the body as its message.</summary>
    public WhatsAppAssistantDocumentSend? Send { get; init; }
    /// <summary>Task 9: after the text the conversation goes to Atención manual, marked «Necesita una persona» with this reason.</summary>
    public WhatsAppAssistantDocumentHandoff? HandOff { get; init; }
    public override string ToString() => "WhatsAppAssistantTerminalResponse { Content = [REDACTED] }";
}
