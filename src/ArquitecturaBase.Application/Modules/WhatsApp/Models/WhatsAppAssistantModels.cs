using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record RequestWhatsAppAssistantSuggestionRequest(Guid ContactId, Guid RequestId, Guid SourceMessageId, long ExpectedModeRevision)
{
    public override string ToString() => "RequestWhatsAppAssistantSuggestionRequest { Context = [REDACTED] }";
}
public sealed record SendWhatsAppAssistantSuggestionRequest(Guid ContactId, Guid SuggestionRequestId, Guid RequestId, IReadOnlyList<Guid> IncludedFieldIds)
{
    public override string ToString() => "SendWhatsAppAssistantSuggestionRequest { Selection = [REDACTED] }";
}
public sealed record WhatsAppAssistantSuggestionResponse(Guid RequestId, Guid SourceMessageId, WhatsAppAssistantRequestState State, WhatsAppAssistantFormProposalResponse? Proposal, WhatsAppOutgoingMessageResponse? Outgoing,
    // Casos del asistente (H2 with a text): only a suggestion that is a text carries it.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WhatsAppAssistantTextProposalResponse? TextProposal = null)
{
    public override string ToString() => "WhatsAppAssistantSuggestionResponse { Content = [REDACTED] }";
}

/// <summary>
/// Casos del asistente: in Atención manual the suggestion can be a text, the case's exact text or a checked draft, with the case it
/// followed and what it used. «Usar como borrador» puts it in the composer and discards the suggestion; nothing goes out on its own.
/// </summary>
public sealed record WhatsAppAssistantTextProposalResponse(string Text, WhatsAppAssistantCaseSource CaseSource, string? CaseTitle, IReadOnlyList<string> Used)
{
    public override string ToString() => "WhatsAppAssistantTextProposalResponse { Content = [REDACTED] }";
}
public sealed record WhatsAppAssistantFormProposalResponse(Guid? ProposalId, Guid DefinitionId, Guid VersionId, int VersionNumber,
    string Name, string Culture, string Reason, IReadOnlyList<WhatsAppAssistantPrefillResponse> Fields)
{
    public override string ToString() => "WhatsAppAssistantFormProposalResponse { Content = [REDACTED] }";
}
public sealed record WhatsAppAssistantPrefillResponse(Guid FieldId, string Label, string DisplayValue)
{
    public override string ToString() => "WhatsAppAssistantPrefillResponse { Content = [REDACTED] }";
}
public sealed record WhatsAppAssistantExplanationResponse(string Reason, IReadOnlyList<WhatsAppAssistantPrefillResponse> ProposedFields)
{
    public override string ToString() => "WhatsAppAssistantExplanationResponse { Content = [REDACTED] }";
}

public sealed record WhatsAppAssistantSuggestionPointerResponse(Guid RequestId, Guid SourceMessageId, WhatsAppAssistantRequestState State);
