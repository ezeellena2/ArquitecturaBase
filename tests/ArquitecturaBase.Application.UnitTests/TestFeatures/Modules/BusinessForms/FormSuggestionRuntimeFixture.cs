using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormSuggestionRuntimeFixture
{
    public FormAssistantFixture Assistant { get; } = new();
    public FormSuggestionFixture Suggestions { get; } = new();
    public FakeFormResponseReader Responses { get; } = new();
    public Guid SourceId { get; } = Guid.NewGuid();
    public Guid SourceFieldId { get; } = Guid.NewGuid();
    public FormSuggestion Suggestion { get; private set; }
    public BusinessFormSuggestionSet Set { get; private set; }
    public FormSuggestionCandidatePolicy Candidates { get; }
    public FormSuggestionRuntimePolicy Policy { get; }
    public FormSuggestionRuntimeFixture(string mode = "Always")
    {
        var target = Assistant.Rows[0] with { OfferWhen = "" }; Assistant.Rows[0] = target;
        var targetSchema = FormDraftPolicy.Read<FormVersionSchema>(target.SchemaJson);
        var sourceField = targetSchema.Questions[0] with { FieldId = SourceFieldId, Key = "field_" + SourceFieldId.ToString("N") };
        var source = new FormRecordStoredRow(Guid.NewGuid(), SourceId, 12, Suggestions.Definition.Id, "VISIT", Suggestions.Options!.Source.VersionId, 1,
            target.NameJson, FormDraftPolicy.Serialize(new FormVersionSchema(1, [sourceField])),
            FormDraftPolicy.Serialize(new[] { new { FieldId = SourceFieldId, Value = Assistant.Catalog.Items[0].Id } }),
            [new(SourceId, SourceFieldId, Assistant.Catalog.Catalog.Id, Assistant.Catalog.Catalog.Revision, Assistant.Catalog.Items[0].Id, "Ana", null, "Personas")]);
        Responses.ExportRows = [new(source, new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc), new(Assistant.UserId, "Recipient"))];
        Suggestions.Options = Suggestions.Options with { Destinations = [new(target.DefinitionId, target.VersionId,
            FormDraftPolicy.Read<Dictionary<string,string>>(target.NameJson), target.Culture, targetSchema.Questions)] };
        Suggestion = new(Guid.NewGuid(), target.DefinitionId, mode, mode == "Assistant" ? "Cuando necesite seguimiento" : null, true, [],
            [new(new("Field", SourceFieldId), Assistant.Catalog.FieldId)]) { DestinationVersionId = target.VersionId };
        Set = Save([Suggestion]);
        Candidates = new(Responses, Suggestions, Assistant, Assistant.Candidates);
        Policy = new(Candidates, Assistant.Catalog.Policy, Suggestions, Assistant.Candidates, new(Assistant.Users, Assistant.Permissions));
    }
    public BusinessFormSuggestionSet Save(IReadOnlyList<FormSuggestion> rows)
    {
        var set = BusinessFormSuggestionSet.Create(Suggestions.Definition.Id, Suggestions.Options!.Source.VersionId,
            Suggestions.Sets.Count + 1, "es", FormDraftPolicy.Serialize(rows));
        Suggestions.Sets.Add(set); Suggestions.Definition.SetActiveSuggestionSetId(set.Id); Set = set;
        return set;
    }
    public StructuredFormSuggestionPreparationRequest Request => new(Assistant.UserId, "es", SourceId, Set.Id, Suggestion.Id);
}
