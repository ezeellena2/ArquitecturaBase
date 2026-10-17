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
        var candidate = Assert.Single((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value);
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
        var candidate = Assert.Single((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value);
        Assert.Equal(fixture.Rows[0].DefinitionRevision, candidate.Form.DefinitionRevision);
        Assert.DoesNotContain("definitionRevision", System.Text.Json.JsonSerializer.Serialize(candidate.Form, System.Text.Json.JsonSerializerOptions.Web), StringComparison.Ordinal);
    }
    [Fact]
    public async Task The_operators_permission_does_not_grant_the_recipient_access()
    {
        var fixture = new FormAssistantFixture();
        fixture.Permissions.Permissions.Clear();
        fixture.Permissions.Permissions[Guid.CreateVersion7()] = [BusinessFormsPermissions.RecordsCreate];
        Assert.Empty((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value);
        Assert.Equal(0, fixture.Reads);
    }
    [Fact]
    public async Task Inactive_accounts_do_not_disclose_candidates_even_if_they_still_have_role_permissions()
    {
        var fixture = new FormAssistantFixture();
        await fixture.Users.SetActiveAsync(fixture.UserId, false, Ct);
        Assert.Empty((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value);
        Assert.Equal(0, fixture.Reads);
    }
    [Fact]
    public async Task Permission_revoked_during_the_read_removes_the_entire_result()
    {
        var fixture = new FormAssistantFixture();
        fixture.OnRead = () => fixture.Permissions.Permissions.Clear();
        Assert.Empty((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value);
    }
    [Fact]
    public async Task Files_are_not_prefill_fields_and_empty_offer_instructions_disable_a_candidate()
    {
        var fixture = new FormAssistantFixture();
        var schema = FormDraftPolicy.Read<FormVersionSchema>(fixture.Rows[0].SchemaJson);
        var id = Guid.CreateVersion7();
        var photo = new CanonicalFormField(id, "field_" + id.ToString("N"), "photo", FormCatalogPreparationFixture.Texts, null, true, null, null, Files: new(1, 1, "camera"));
        fixture.Rows[0] = fixture.Rows[0] with { SchemaJson = FormDraftPolicy.Serialize(schema with { Questions = [.. schema.Questions, photo] }), QuestionCount = 2 };
        Assert.Single(Assert.Single((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value).Questions);
        fixture.Rows[0] = fixture.Rows[0] with { OfferWhen = "" };
        Assert.Empty((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).Value);
    }
    [Fact]
    public async Task An_incomplete_catalog_fails_safely_instead_of_becoming_a_missing_form()
    {
        var fixture = new FormAssistantFixture();
        fixture.Catalog.Items.Clear();
        Assert.True((await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct)).IsFailure);
    }
    [Fact]
    public async Task Cancellation_is_honored_before_reading()
    {
        var fixture = new FormAssistantFixture();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Candidates.ListAsync(fixture.UserId, "es", cancelled.Token));
        Assert.Equal(0, fixture.Reads);
    }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
