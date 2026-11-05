using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Models.StructuredData;

public enum StructuredAuthoringKind { Form, Catalog, Declined }

/// <summary>
/// What a channel shows of an authoring proposal (IA3-A): the title, up to ten lines and how many more there are, in the
/// person's language. A declined one carries its reason and, when it names something that exists, the app path to it.
/// </summary>
public sealed record StructuredAuthoringSummary(StructuredAuthoringKind Kind, string Title, IReadOnlyList<string> Lines, int MoreCount)
{
    public string? Reason { get; init; }
    public string? AppPath { get; init; }
    public override string ToString() => "StructuredAuthoringSummary { Content = [REDACTED] }";
}

public sealed record StructuredAuthoringProposalRequest(AssistantChannelContext Context, AssistantObservedProposal Proposal,
    AssistantOperationProvenance Provenance)
{ public override string ToString() => "StructuredAuthoringProposalRequest { Content = [REDACTED] }"; }

/// <summary>What the button created, inactive or in draft, and the app path where the person reviews it.</summary>
public sealed record StructuredAuthoringCreatedResponse(StructuredAuthoringKind Kind, Guid Id, string Name, string AppPath)
{ public override string ToString() => "StructuredAuthoringCreatedResponse { Content = [REDACTED] }"; }
