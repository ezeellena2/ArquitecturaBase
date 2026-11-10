using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Models.StructuredData;

/// <summary>What an authoring proposal is: a form, a catalog, why nothing is built or, with a spreadsheet, an import into a catalog.</summary>
public enum StructuredAuthoringKind { Form, Catalog, Declined, Import }

/// <summary>
/// What a channel shows of an authoring proposal (IA3-A): the title, up to ten lines and how many more there are, in the
/// person's language. A declined one carries its reason and, when it names something that exists, the app path to it. A catalog or
/// an import with a spreadsheet says how many rows of data it takes, and an import brings the app path where it is reviewed.
/// </summary>
public sealed record StructuredAuthoringSummary(StructuredAuthoringKind Kind, string Title, IReadOnlyList<string> Lines, int MoreCount)
{
    public string? Reason { get; init; }
    public string? AppPath { get; init; }
    public int? RowCount { get; init; }
    public override string ToString() => "StructuredAuthoringSummary { Content = [REDACTED] }";
}

/// <summary>A proposal to create, with the operation it comes from: the owner keeps that request with what it creates.</summary>
public sealed record StructuredAuthoringProposalRequest(Guid RequestId, AssistantChannelContext Context, AssistantObservedProposal Proposal,
    AssistantOperationProvenance Provenance)
{ public override string ToString() => "StructuredAuthoringProposalRequest { Content = [REDACTED] }"; }

/// <summary>What the button created, inactive or in draft, and the app path where the person reviews it.</summary>
public sealed record StructuredAuthoringCreatedResponse(StructuredAuthoringKind Kind, Guid Id, string Name, string AppPath)
{ public override string ToString() => "StructuredAuthoringCreatedResponse { Content = [REDACTED] }"; }
