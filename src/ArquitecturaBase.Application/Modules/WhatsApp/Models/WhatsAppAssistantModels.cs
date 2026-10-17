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
public sealed record WhatsAppAssistantSuggestionResponse(Guid RequestId, Guid SourceMessageId, WhatsAppAssistantRequestState State, WhatsAppAssistantFormProposalResponse? Proposal, WhatsAppOutgoingMessageResponse? Outgoing)
{
    public override string ToString() => "WhatsAppAssistantSuggestionResponse { Content = [REDACTED] }";
}
public sealed record WhatsAppAssistantFormProposalResponse(Guid ProposalId, Guid DefinitionId, Guid VersionId, int VersionNumber,
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
