using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormSuggestionAssistantFixture
{
    public FormSuggestionRuntimeFixture Runtime { get; } = new("Assistant");
    public BusinessFormSuggestionAssistantCapabilitySource Source => new(Runtime.Candidates, Runtime.Assistant.Candidates);
    public Guid TurnId { get; } = Guid.NewGuid();
    public AssistantChannelContext Context => Runtime.Assistant.Context with
    {
        AllowedCapabilities = [new(BusinessFormSuggestionAssistantCapabilitySource.OffersReadId, 1),
            new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1), new(BusinessFormSuggestionAssistantCapabilitySource.RegistrationId, 1)]
    };
    public AssistantObservedRead Offers(Guid? subject = null, Guid? turn = null, int version = 1) => new(turn ?? TurnId, 1,
        new(BusinessFormSuggestionAssistantCapabilitySource.OffersReadId, version),
        new StructuredFormOffersPayload(subject ?? Runtime.Assistant.UserId, "es", [], [Runtime.SourceId]));
    public FormSuggestionAssistantCandidatesPayload ReadPayload()
    {
        var form = Runtime.Assistant.ReadPayload().Forms[0] with { OfferWhen = Runtime.Suggestion.When! };
        return new("complete", Runtime.Assistant.UserId, "es", true, Runtime.SourceId, Runtime.Set.Id, Runtime.Set.Revision,
            "Visita", "VISIT-000012", [new(Runtime.SourceFieldId, "choice", "Persona", "Ana")],
            [new(Runtime.Suggestion.Id, form, [new(Runtime.Assistant.Catalog.FieldId, "choice", Runtime.Assistant.Catalog.Items[0].Id.ToString("D"))], false)]);
    }
    public AssistantObservedRead Read(int ordinal = 2, Guid? turn = null) => new(turn ?? TurnId, ordinal,
        new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1), ReadPayload());
    public FormSuggestionAssistantRegistrationPayload Registration() => new("proposed", 2, Runtime.SourceId, Runtime.Set.Id, Runtime.Set.Revision,
        Runtime.Suggestion.Id, Runtime.Assistant.Rows[0].DefinitionId, Runtime.Assistant.Rows[0].VersionId, Runtime.Assistant.Rows[0].DefinitionRevision,
        [new(Runtime.Assistant.Catalog.FieldId, "choice", Runtime.Assistant.Catalog.Items[0].Id.ToString("D"))], "Pidió seguimiento.");
    public AssistantObservedProposal Proposal() => new(TurnId, Guid.NewGuid(), new(BusinessFormSuggestionAssistantCapabilitySource.RegistrationId, 1), Registration())
        { ObservedReads = [Offers(), Read()] };
}
