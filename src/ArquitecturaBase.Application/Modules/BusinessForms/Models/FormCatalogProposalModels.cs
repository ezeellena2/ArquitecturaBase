using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormCatalogMetadataPayload(string Source, IReadOnlyList<FormCatalogFilterColumnResponse> Columns) : AssistantCapabilityPayload;

public sealed record FormCatalogProposalLimitation(string Code, string Criterion)
{
    public override string ToString() => "FormCatalogProposalLimitation { Content = [REDACTED] }";
}

public sealed record FormCatalogProposalPayload(int MetadataReadOrdinal, string Name, string Description,
    FormCatalogFilterDefinition Filters, IReadOnlyList<FormCatalogProposalLimitation> Limitations) : AssistantCapabilityPayload;

// A people catalog (H4) keeps its filters and its preview; a Manual one, what the assistant proposed for it, without a preview,
// always in version 1's shape. RequestedIn is "app" when the person asked in the app; null, in a channel, as every proposal
// before Home could propose. RequestId is the request of the turn the proposal came from, when it is known.
public sealed record FormCatalogProposalEvidence(string OriginalCulture, Guid RequestedByUserId, DateTime RequestedAtUtc,
    Guid TurnId, string CapabilityId, int CapabilityVersion, AssistantOperationProvenance Provenance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormCatalogProposalPayload? OriginalProposal, int PreviewTotalCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormAuthoringCatalogProposal? Manual = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestedIn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? RequestId = null)
{
    public override string ToString() => "FormCatalogProposalEvidence { Content = [REDACTED] }";
}

public sealed record FormCatalogProposalResponse(string State, string OriginalCulture,
    FormAuthorResponse RequestedBy, DateTime RequestedAtUtc, FormAuthorResponse? DecidedBy, DateTime? DecidedAtUtc,
    AssistantOperationProvenance Provenance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormCatalogProposalPayload? OriginalProposal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormAuthoringCatalogProposal? Manual = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestedIn = null)
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
