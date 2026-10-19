using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed partial class FormAssistantClarificationPolicyTests
{
    [Fact]
    public async Task Every_button_carries_the_authentic_definition_revision_without_selecting_a_form()
    {
        var f = new FormAssistantClarificationFixture();
        var result = await f.Policy.ValidateAsync(new(f.Inner.UserId, "es", f.Proposal()), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(f.Inner.Rows.Select(row => row.DefinitionRevision), result.Value.Choices.Select(choice => choice.DefinitionRevision));
        Assert.Null(result.Value.SelectedForm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_observed_choice_uses_the_same_proposal_and_current_catalog_without_inventing_prefill(bool forSending)
    {
        var f = new FormAssistantClarificationFixture();
        var result = await f.Policy.ValidateAsync(Select(f, forSending), Ct);
        Assert.True(result.IsSuccess);
        var selected = Assert.IsType<StructuredFormPrefillResponse>(result.Value.SelectedForm);
        Assert.Equal(f.Inner.Rows[0].DefinitionId, selected.Form.DefinitionId);
        Assert.Equal(f.Inner.Rows[0].VersionId, selected.Form.VersionId);
        Assert.Equal(f.Inner.Rows[0].DefinitionRevision, selected.DefinitionRevision);
        Assert.Empty(selected.Fields);
        Assert.Single(selected.Catalogs);
        Assert.Equal("es", selected.Culture);
        Assert.Equal("Eligió este formulario entre las opciones ofrecidas.", selected.Reason);
        Assert.Equal(forSending ? 1 : 0, f.Inner.Catalog.Locks.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_choice_outside_the_authentic_question_is_rejected_even_if_it_is_a_valid_guid(bool empty)
    {
        var f = new FormAssistantClarificationFixture();
        var result = await f.Policy.ValidateAsync(Select(f, true) with { SelectedDefinitionId = empty ? Guid.Empty : Guid.CreateVersion7() }, Ct);
        Assert.True(result.IsFailure);
        Assert.Empty(f.Inner.Catalog.Locks);
    }

    [Fact]
    public async Task A_button_revalidates_the_current_definition_revision_before_preparing_the_flow()
    {
        var f = new FormAssistantClarificationFixture(); var request = Select(f, true);
        f.Inner.Rows[0] = f.Inner.Rows[0] with { DefinitionRevision = f.Inner.Rows[0].DefinitionRevision + 1 };
        var result = await f.Policy.ValidateAsync(request, Ct);
        Assert.Equal(FormDraftErrors.RevisionConflict, result.Error);
        Assert.Empty(f.Inner.Catalog.Locks);
    }

    [Fact]
    public async Task A_button_does_not_borrow_the_operators_permission_after_the_subject_loses_create()
    {
        var f = new FormAssistantClarificationFixture(); var request = Select(f, true);
        f.Inner.Permissions.Permissions.Clear();
        var result = await f.Policy.ValidateAsync(request, Ct);
        Assert.Equal(FormRecordErrors.AuthorizationChanged, result.Error);
        Assert.Empty(f.Inner.Catalog.Locks);
    }

    [Theory]
    [InlineData(false, "en", "The recipient selected this form from the offered options.")]
    [InlineData(true, "es", "Eligió este formulario entre las opciones ofrecidas.")]
    public async Task The_selected_form_carries_one_complete_language_including_its_fixed_selection_reason(bool incompleteEnglish, string expectedCulture, string expectedReason)
    {
        var f = new FormAssistantClarificationFixture(); await f.UseEnglishAsync(Ct);
        if (incompleteEnglish)
            f.Inner.Catalog.Items[0] = BusinessFormCatalogItem.Create(f.Inner.Catalog.Catalog.Id, "A", "Ana", null, null, null, 0);
        var result = await f.Policy.ValidateAsync(Select(f, false), Ct);
        Assert.True(result.IsSuccess);
        var selected = Assert.IsType<StructuredFormPrefillResponse>(result.Value.SelectedForm);
        Assert.Equal(expectedCulture, selected.Culture);
        Assert.Equal(expectedReason, selected.Reason);
        Assert.Empty(f.Inner.Catalog.Locks);
    }

    [Fact]
    public async Task A_choice_whose_catalog_became_unavailable_never_produces_a_selected_flow()
    {
        var f = new FormAssistantClarificationFixture(); var request = Select(f, true);
        f.Inner.Catalog.Items.Clear();
        var result = await f.Policy.ValidateAsync(request, Ct);
        Assert.True(result.IsFailure);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_selected_clarification_rechecks_the_common_language_of_later_rules(bool forSending)
    {
        var f = new FormAssistantClarificationFixture();
        await f.UseEnglishAsync(Ct);
        f.Inner.Catalog.SetRules([new(Guid.NewGuid(), "Approved", [], [new(Guid.NewGuid(), "NotifyAuthor", null,
            new Dictionary<string, string> { ["es"] = "Aprobamos la visita" }, false)])]);
        var result = await f.Policy.ValidateAsync(Select(f, forSending), Ct);
        Assert.True(result.IsSuccess);
        var selected = Assert.IsType<StructuredFormPrefillResponse>(result.Value.SelectedForm);
        Assert.Equal("es", selected.Culture);
        Assert.Equal("Personas", Assert.Single(selected.Catalogs).Label);
        Assert.Equal("Ana", Assert.Single(Assert.Single(selected.Catalogs).Options).Text);
    }

    private static StructuredFormClarificationRequest Select(FormAssistantClarificationFixture f, bool forSending) =>
        new(f.Inner.UserId, f.Culture, f.Proposal()) { SelectedDefinitionId = f.Inner.Rows[0].DefinitionId, ForSending = forSending };
}
