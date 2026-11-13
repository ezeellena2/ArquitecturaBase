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
    /// <summary>VC1-A (task 29): the body is the notice of the loads of a spreadsheet, which go after it (none, or up to three).</summary>
    public IReadOnlyList<WhatsAppAssistantSpreadsheetLoad>? Loads { get; init; }
    /// <summary>VC1-A (task 29): «Por qué lo propuso» of the notice of the loads.</summary>
    public string? SpreadsheetRowsJson { get; init; }
    public override string ToString() => "WhatsAppAssistantTerminalResponse { Content = [REDACTED] }";
}
