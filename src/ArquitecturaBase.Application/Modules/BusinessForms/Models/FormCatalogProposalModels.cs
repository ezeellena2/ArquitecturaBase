using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormCatalogMetadataPayload(string Source, IReadOnlyList<FormCatalogFilterColumnResponse> Columns) : AssistantCapabilityPayload;

public sealed record FormCatalogProposalLimitation(string Code, string Criterion)
{
    public override string ToString() => "FormCatalogProposalLimitation { Content = [REDACTED] }";
}

public sealed record FormCatalogProposalPayload(int MetadataReadOrdinal, string Name, string Description,
    FormCatalogFilterDefinition Filters, IReadOnlyList<FormCatalogProposalLimitation> Limitations) : AssistantCapabilityPayload;

public sealed record FormCatalogProposalEvidence(string OriginalCulture, Guid RequestedByUserId, DateTime RequestedAtUtc,
    Guid TurnId, string CapabilityId, int CapabilityVersion, AssistantOperationProvenance Provenance,
    FormCatalogProposalPayload OriginalProposal, int PreviewTotalCount)
{
    public override string ToString() => "FormCatalogProposalEvidence { Content = [REDACTED] }";
}

public sealed record FormCatalogProposalResponse(string State, string OriginalCulture,
    FormAuthorResponse RequestedBy, DateTime RequestedAtUtc, FormAuthorResponse? DecidedBy, DateTime? DecidedAtUtc,
    AssistantOperationProvenance Provenance, FormCatalogProposalPayload OriginalProposal)
{
    public override string ToString() => "FormCatalogProposalResponse { Content = [REDACTED] }";
}

public sealed record ApproveFormCatalogProposalRequest(long? ExpectedRevision,
    IReadOnlyDictionary<string,string?>? Name, IReadOnlyDictionary<string,string?>? Description,
    FormCatalogFilterDefinition? Filters, Guid? SampleUserId)
{
    public override string ToString() => "ApproveFormCatalogProposalRequest { Content = [REDACTED] }";
}

public sealed record DiscardFormCatalogProposalRequest(long? ExpectedRevision);

internal sealed record FormCatalogProposalArguments(string Name, string Description, FormCatalogFilterDefinition Filters,
    IReadOnlyList<FormCatalogProposalLimitation> Limitations)
{ public override string ToString() => "FormCatalogProposalArguments { Content = [REDACTED] }"; }
