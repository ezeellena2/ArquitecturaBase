using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormAssistantCandidatePolicyTests
{
    [Fact]
    public async Task Eligible_candidates_use_the_sealed_version_and_recipient_options_without_taking_locks()
    {
        var fixture = new FormAssistantFixture();
        var candidate = Assert.Single((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value.Forms);
        Assert.Equal(fixture.Rows[0].DefinitionRevision, candidate.DefinitionRevision);
        Assert.Equal(fixture.Rows[0].OfferWhen, candidate.OfferWhen);
        Assert.Equal(fixture.Catalog.Version.Id, candidate.Form.VersionId);
        Assert.Equal(fixture.Catalog.FieldId, Assert.Single(candidate.Questions).FieldId);
        Assert.Equal("Ana", Assert.Single(Assert.Single(candidate.Catalogs).Options).Text);
        Assert.Empty(fixture.Catalog.Locks);
    }
    [Fact]
    public async Task A_selection_exposes_the_authoritative_definition_revision_without_publishing_it_over_the_wire()
    {
        var fixture = new FormAssistantFixture();
        var candidate = Assert.Single((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value.Forms);
        Assert.Equal(fixture.Rows[0].DefinitionRevision, candidate.Form.DefinitionRevision);
        Assert.DoesNotContain("definitionRevision", System.Text.Json.JsonSerializer.Serialize(candidate.Form, System.Text.Json.JsonSerializerOptions.Web), StringComparison.Ordinal);
    }
    [Fact]
    public async Task The_operators_permission_does_not_grant_the_recipient_access()
    {
        var fixture = new FormAssistantFixture();
        fixture.Permissions.Permissions.Clear();
        fixture.Permissions.Permissions[Guid.CreateVersion7()] = [BusinessFormsPermissions.RecordsCreate];
        Assert.Empty((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value.Forms);
        Assert.Equal(0, fixture.Reads);
    }
    [Fact]
    public async Task Inactive_accounts_do_not_disclose_candidates_even_if_they_still_have_role_permissions()
    {
        var fixture = new FormAssistantFixture();
        await fixture.Users.SetActiveAsync(fixture.UserId, false, Ct);
        Assert.Empty((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value.Forms);
        Assert.Equal(0, fixture.Reads);
    }
    [Fact]
    public async Task Permission_revoked_during_the_read_removes_the_entire_result()
    {
        var fixture = new FormAssistantFixture();
        fixture.OnRead = () => fixture.Permissions.Permissions.Clear();
        Assert.Empty((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value.Forms);
    }
    [Fact]
    public async Task Files_are_not_prefill_fields_and_empty_offer_instructions_disable_a_candidate()
    {
        var fixture = new FormAssistantFixture();
        var schema = FormDraftPolicy.Read<FormVersionSchema>(fixture.Rows[0].SchemaJson);
        var id = Guid.CreateVersion7();
        var photo = new CanonicalFormField(id, "field_" + id.ToString("N"), "photo", FormCatalogPreparationFixture.Texts, null, true, null, null, Files: new(1, 1, "camera"));
        fixture.Rows[0] = fixture.Rows[0] with { SchemaJson = FormDraftPolicy.Serialize(schema with { Questions = [.. schema.Questions, photo] }), QuestionCount = 2 };
        Assert.Single(Assert.Single((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value.Forms).Questions);
        fixture.Rows[0] = fixture.Rows[0] with { OfferWhen = "" };
        Assert.Empty((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value.Forms);
    }
    [Fact]
    public async Task A_form_that_cannot_be_prepared_is_not_accessible_to_the_recipient()
    {
        var fixture = new FormAssistantFixture();
        fixture.Catalog.Items.Clear();
        var result = await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Forms);
    }
    [Fact]
    public async Task Cancellation_is_honored_before_reading()
    {
        var fixture = new FormAssistantFixture();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Candidates.ListAsync(fixture.UserId, "es", cancelled.Token));
        Assert.Equal(0, fixture.Reads);
    }
    [Fact]
    public void The_questions_of_a_block_of_rows_are_not_offered_to_prefill()
    {
        var supplier = Guid.CreateVersion7();
        var schema = new FormVersionSchema(1, [Question(supplier, "shortText") with { MaxLength = 80 },
            Question(Guid.CreateVersion7(), "group") with { Required = true, Rows = new(1, 20) },
            Question(Guid.CreateVersion7(), "decimal") with { Repeated = true }]);
        var row = new FormAssistantCandidateRow(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "REC", """{"es":"Recepción"}""", """{"es":"Enviar"}""",
            "es", 2, 1, "Cuando llegue mercadería", FormDraftPolicy.Serialize(schema));

        var candidate = FormAssistantCandidatePolicy.Project(row, new([], []) { Culture = "es" });

        Assert.Equal([supplier], candidate.Value.Questions.Select(question => question.FieldId));
    }
    // LD2-A: a question that chooses first by a field starts with its list empty, so it is not prefilled either.
    [Fact]
    public void A_question_that_chooses_first_by_a_field_is_not_offered_to_prefill()
    {
        var supplier = Guid.CreateVersion7();
        var catalog = Guid.CreateVersion7();
        var schema = new FormVersionSchema(1, [Question(supplier, "choice") with { Source = new(catalog) },
            Question(Guid.CreateVersion7(), "choice") with { Source = new(catalog, GroupBy: Guid.CreateVersion7()) }]);
        var row = new FormAssistantCandidateRow(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "PED", """{"es":"Pedido"}""", """{"es":"Enviar"}""",
            "es", 2, 1, "Cuando falte un insumo", FormDraftPolicy.Serialize(schema));

        var candidate = FormAssistantCandidatePolicy.Project(row, new([], []) { Culture = "es" });

        Assert.Equal([supplier], candidate.Value.Questions.Select(question => question.FieldId));
    }
    // W1-A: a person sends a form with a location question from the chat, which asks for the location before its Flow; the
    // assistant does not offer it, nor suggest it, in this stage.
    [Fact]
    public void A_form_with_a_location_question_is_not_a_candidate_of_the_assistant()
    {
        var schema = new FormVersionSchema(1, [Question(Guid.CreateVersion7(), "shortText") with { MaxLength = 80 },
            Question(Guid.CreateVersion7(), "location") with { Required = true }]);
        var row = new FormAssistantCandidateRow(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "VIS", """{"es":"Visita"}""", """{"es":"Enviar"}""",
            "es", 2, 1, "Cuando visite un cliente", FormDraftPolicy.Serialize(schema));

        Assert.True(FormAssistantCandidatePolicy.Project(row, new([], []) { Culture = "es" }).IsFailure);
    }
    private static CanonicalFormField Question(Guid id, string type) =>
        new(id, "field_" + id.ToString("N"), type, new Dictionary<string, string> { ["es"] = "Pregunta" }, null, false, null, null);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
