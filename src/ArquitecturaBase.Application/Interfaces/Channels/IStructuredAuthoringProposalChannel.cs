using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// A channel shows the assistant's authoring proposals (a form, a catalog, an import or why it cannot build one) as a summary and
/// creates them with a button (IA3-A). The owner reads its own payload; the channel never interprets it.
/// </summary>
public interface IStructuredAuthoringProposalChannel
{
    /// <summary>
    /// Null when the proposal is not an authoring one of an installed owner. The request is the proposal's operation: an import
    /// creates nothing, and its summary brings the app path where the person reviews it, with that request and the proposal.
    /// </summary>
    StructuredAuthoringSummary? Summarize(Guid requestId, AssistantObservedProposal proposal, string culture);
    Task<Result<StructuredAuthoringCreatedResponse>> CreateAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken);
}
