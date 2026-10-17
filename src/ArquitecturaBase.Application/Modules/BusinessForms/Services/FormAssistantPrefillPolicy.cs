using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormAssistantPrefillPolicy(FormAssistantCandidatePolicy candidates, IFormSelectionReader reader,
    FormCatalogPreparationPolicy preparation)
{
    public async Task<Result<StructuredFormPrefillResponse>> ValidateAsync(StructuredFormPrefillRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!await candidates.IsEligibleAsync(request.RecipientUserId, request.Culture, ct)) return FormRecordErrors.AuthorizationChanged;
        var proposal = request.Proposal;
        if (proposal.Id == Guid.Empty || proposal.TurnId == Guid.Empty
            || proposal.Capability is not { Id: BusinessFormAssistantCapabilitySource.RegistrationId, Version: BusinessFormAssistantCapabilitySource.Version }
            || proposal.Payload is not FormAssistantRegistrationPayload payload || payload.State != "proposed"
            || !FormAssistantCapabilityCodec.Text(payload.Reason, 500)) return FormRecordErrors.InvalidResponse;
        var observed = FormAssistantObservationPolicy.Find(proposal.ObservedReads, proposal.TurnId, request.RecipientUserId, request.Culture,
            payload.DefinitionId, payload.VersionId);
        if (observed.IsFailure) return observed.Error;
        if (observed.Value.ReadOrdinal != payload.ReadOrdinal || observed.Value.Form.DefinitionRevision != payload.ExpectedDefinitionRevision)
            return FormRecordErrors.InvalidResponse;
        var validated = FormAssistantObservationPolicy.Normalize(observed.Value.Form, payload.Values, request.Culture);
        if (validated.IsFailure) return validated.Error;
        var included = request.IncludedFieldIds ?? validated.Value.Select(value => value.FieldId).ToArray();
        if (included.Distinct().Count() != included.Count || included.Any(id => !validated.Value.Any(value => value.FieldId == id)))
            return FormRecordErrors.InvalidResponse;
        var row = await reader.FindAssistantAsync(payload.DefinitionId, payload.VersionId, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.OfferWhen)) return FormRecordErrors.InvalidResponse;
        if (row.DefinitionRevision != payload.ExpectedDefinitionRevision) return FormDraftErrors.RevisionConflict;
        var prepared = request.ForSending
            ? FormAssistantCandidatePolicy.Project(row, await preparation.PrepareAsync(request.RecipientUserId, request.Culture, row.DefinitionId, row.VersionId, true, ct))
            : await candidates.PrepareAsync(row, request.RecipientUserId, request.Culture, ct);
        if (prepared.IsFailure) return prepared.Error;
        var current = prepared.Value;
        var culture = FormAssistantCandidatePolicy.PresentationCulture(current, request.Culture);
        var fields = new List<StructuredFormPrefillField>();
        foreach (var value in validated.Value.Where(value => included.Contains(value.FieldId)))
        {
            var question = current.Questions.SingleOrDefault(question => question.FieldId == value.FieldId);
            var original = observed.Value.Form.Questions.Single(question => question.FieldId == value.FieldId);
            if (question is null || question.Type != value.Type || question.SourceCatalogId != original.CatalogId) return FormRecordErrors.InvalidResponse;
            var catalog = current.Catalogs.SingleOrDefault(catalog => catalog.FieldId == value.FieldId);
            if (catalog is not null && catalog.Revision != original.CatalogRevision) return FormRecordErrors.InvalidResponse;
            var canonical = FormScalarPrefillPolicy.Normalize(question, value.Value, current.Catalogs);
            if (canonical.IsFailure) return canonical.Error;
            var display = canonical.Value;
            if (question.Type == "choice")
            {
                var id = Guid.ParseExact(canonical.Value, "D");
                if (catalog is not null)
                {
                    var option = catalog.Options.Single(option => option.Id == id);
                    display = option.Translations is { } texts && texts.TryGetValue(culture, out var text) ? text.Text : option.Text;
                }
                else display = FormAssistantCandidatePolicy.Text(question.Options!.Single(option => option.ChoiceId == id).Label, culture, current.OriginalCulture);
            }
            fields.Add(new(question.FieldId, question.Key, question.Type,
                FormAssistantCandidatePolicy.Text(question.Label, culture, current.OriginalCulture), canonical.Value, display));
        }
        // Preview has no locks; recheck the mutable definition and current identity before disclosing fields.
        var final = await reader.FindAssistantAsync(payload.DefinitionId, payload.VersionId, ct);
        if (!await candidates.IsEligibleAsync(request.RecipientUserId, request.Culture, ct)) return FormRecordErrors.AuthorizationChanged;
        if (final is null || final.DefinitionRevision != row.DefinitionRevision) return FormDraftErrors.RevisionConflict;
        return new StructuredFormPrefillResponse(current.Form, row.DefinitionRevision, fields, current.Catalogs, payload.Reason, culture);
    }
}
