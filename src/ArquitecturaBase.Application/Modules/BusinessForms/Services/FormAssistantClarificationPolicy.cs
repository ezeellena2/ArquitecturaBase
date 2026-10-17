using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormAssistantClarificationPolicy(FormAssistantCandidatePolicy candidates, IFormSelectionReader reader)
{
    public async Task<Result<StructuredFormClarificationResponse>> ValidateAsync(StructuredFormClarificationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!await candidates.IsEligibleAsync(request.RecipientUserId, request.Culture, ct)) return FormRecordErrors.AuthorizationChanged;
        var proposal = request.Proposal;
        if (proposal.Id == Guid.Empty || proposal.TurnId == Guid.Empty
            || proposal.Capability is not { Id: BusinessFormAssistantCapabilitySource.ClarificationId, Version: BusinessFormAssistantCapabilitySource.Version }
            || proposal.Payload is not FormAssistantClarificationPayload payload || !FormAssistantCapabilityCodec.ValidClarification(payload))
            return FormRecordErrors.InvalidResponse;
        var choices = new List<StructuredFormClarificationChoice>();
        foreach (var selection in payload.Forms)
        {
            var observed = FormAssistantObservationPolicy.Find(proposal.ObservedReads, proposal.TurnId, request.RecipientUserId, request.Culture,
                selection.DefinitionId, selection.VersionId);
            if (observed.IsFailure) return observed.Error;
            if (observed.Value.ReadOrdinal != payload.ReadOrdinal || observed.Value.Form.DefinitionRevision != selection.ExpectedDefinitionRevision)
                return FormRecordErrors.InvalidResponse;
            var row = await reader.FindAssistantAsync(selection.DefinitionId, selection.VersionId, ct);
            if (row is null || string.IsNullOrWhiteSpace(row.OfferWhen)) return FormRecordErrors.InvalidResponse;
            if (row.DefinitionRevision != selection.ExpectedDefinitionRevision) return FormDraftErrors.RevisionConflict;
            var current = await candidates.PrepareAsync(row, request.RecipientUserId, request.Culture, ct);
            if (current.IsFailure) return current.Error;
            choices.Add(new(row.DefinitionId, row.VersionId,
                FormAssistantCandidatePolicy.Text(current.Value.Form.Name, request.Culture, current.Value.OriginalCulture)));
        }
        foreach (var selection in payload.Forms)
        {
            var current = await reader.FindAssistantAsync(selection.DefinitionId, selection.VersionId, ct);
            if (current is null || string.IsNullOrWhiteSpace(current.OfferWhen) || current.DefinitionRevision != selection.ExpectedDefinitionRevision)
                return FormDraftErrors.RevisionConflict;
        }
        if (!await candidates.IsEligibleAsync(request.RecipientUserId, request.Culture, ct)) return FormRecordErrors.AuthorizationChanged;
        return new StructuredFormClarificationResponse(FormAssistantTexts.ClarificationQuestion(request.Culture), choices);
    }
}
