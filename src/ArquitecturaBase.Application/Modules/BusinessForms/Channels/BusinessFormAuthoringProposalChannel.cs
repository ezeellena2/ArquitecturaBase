using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

// IA3-A: a channel shows an authoring proposal as its own summary and creates it with a button; creating is the same as from
// Home, with the channel's person, and returns the app path where that person reviews and publishes it.
internal sealed class BusinessFormAuthoringProposalChannel(IFormDraftProposalService drafts, IFormCatalogAssistantProposalService catalogs)
    : IStructuredAuthoringProposalChannel
{
    public StructuredAuthoringSummary? Summarize(AssistantObservedProposal proposal, string culture) => proposal switch
    {
        { Capability: { Id: BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Version: 1 }, Payload: FormAuthoringFormProposal form } =>
            FormAuthoringSummaryPolicy.Form(form, culture),
        { Capability: { Id: BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, Version: 1 }, Payload: FormAuthoringCatalogProposal manual } =>
            FormAuthoringSummaryPolicy.Catalog(manual, culture),
        { Capability: { Id: BusinessFormCatalogAssistantCapabilitySource.ProposeId, Version: 1 }, Payload: FormCatalogProposalPayload people } =>
            FormAuthoringSummaryPolicy.People(people, culture),
        { Capability: { Id: BusinessFormAuthoringAssistantCapabilitySource.DeclineId, Version: 1 }, Payload: FormAuthoringDecline decline } =>
            FormAuthoringSummaryPolicy.Declined(decline),
        _ => null,
    };

    public async Task<Result<StructuredAuthoringCreatedResponse>> CreateAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (request.Proposal.Payload)
        {
            case FormAuthoringFormProposal form:
                var draft = await drafts.CreateFromChannelAsync(request, cancellationToken);
                if (draft.IsFailure) return draft.Error;
                return new StructuredAuthoringCreatedResponse(StructuredAuthoringKind.Form, draft.Value.DefinitionId!.Value, form.Name,
                    FormAuthoringSummaryPolicy.FormPath(draft.Value.DefinitionId.Value));
            case FormAuthoringCatalogProposal or FormAuthoringMappedCatalogProposal or FormCatalogProposalPayload:
                var catalog = await catalogs.CreateFromChannelAsync(request, cancellationToken);
                if (catalog.IsFailure) return catalog.Error;
                var name = request.Proposal.Payload switch
                {
                    FormAuthoringCatalogProposal manual => manual.Name,
                    FormAuthoringMappedCatalogProposal mapped => mapped.Name,
                    _ => ((FormCatalogProposalPayload)request.Proposal.Payload).Name,
                };
                return new StructuredAuthoringCreatedResponse(StructuredAuthoringKind.Catalog, catalog.Value.CatalogId!.Value, name,
                    FormAuthoringSummaryPolicy.CatalogPath(catalog.Value.CatalogId.Value));
            default:
                return FormCatalogErrors.ProposalUnavailable;
        }
    }
}
