using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredCatalogProposalAcceptanceRequest(AssistantChannelContext Context,
    AssistantObservedProposal Proposal, AssistantOperationProvenance Provenance)
{ public override string ToString() => "StructuredCatalogProposalAcceptanceRequest { Content = [REDACTED] }"; }

public sealed record StructuredCatalogProposalAcceptedResponse(Guid CatalogId, string State, string Name,
    string OriginalCulture, int PreviewTotalCount, IReadOnlyList<StructuredCatalogProposalLimitation> Limitations)
{ public override string ToString() => "StructuredCatalogProposalAcceptedResponse { Content = [REDACTED] }"; }

public sealed record StructuredCatalogProposalLimitation(string Code, string Criterion)
{ public override string ToString() => "StructuredCatalogProposalLimitation { Content = [REDACTED] }"; }
