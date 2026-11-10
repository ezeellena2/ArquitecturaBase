using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

// A neutral owner of authoring proposals (IA3-A): it summarizes a fictitious form and records what the button creates, so the
// channel is proven with PostgreSQL without naming BusinessForms. The real owner is proven in its own module.
internal sealed class WhatsAppAuthoringChannelDouble : IStructuredAuthoringProposalChannel
{
    public const string ProposeFormId = "forms.authoring.propose-form";
    public const string DeclineId = "forms.authoring.decline";
    public List<StructuredAuthoringProposalRequest> Created { get; } = [];
    public Guid DefinitionId { get; } = Guid.NewGuid();

    // A decline here is always «it is not clear what to build», the one that asks and waits.
    public StructuredAuthoringSummary? Summarize(Guid requestId, AssistantObservedProposal proposal, string culture) => proposal.Capability.Id switch
    {
        ProposeFormId => new(StructuredAuthoringKind.Form, "Parte de lluvias · LLU", ["1. Fecha · fecha", "2. Milímetros · número"], 0),
        DeclineId => new(StructuredAuthoringKind.Declined, "", [], 0) { Reason = "unclear" },
        _ => null,
    };

    public Task<Result<StructuredAuthoringCreatedResponse>> CreateAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken)
    {
        lock (Created) Created.Add(request);
        return Task.FromResult(Result.Success(new StructuredAuthoringCreatedResponse(StructuredAuthoringKind.Form, DefinitionId, "Parte de lluvias",
            $"/formularios/{DefinitionId:D}")));
    }
}
