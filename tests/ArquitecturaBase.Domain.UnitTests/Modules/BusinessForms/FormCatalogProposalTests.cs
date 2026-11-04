using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogProposalTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 18, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ProposalId = new("7c9d3750-6d2d-4df7-aad4-199d92fda45e");
    private static readonly Guid ActorId = new("8373c8e5-1e0d-4878-93eb-394e81f597e2");
    private static Dictionary<string,string> Text(string culture, string value) => new() { [culture] = value };
    private static BusinessFormCatalog New() => BusinessFormCatalog.CreateProposedFilters("es", Text("es", "Supervisores"), Text("es", "Personas activas"), "{}", ProposalId, "{\"originalCulture\":\"es\"}");

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void A_proposal_starts_inactive_with_original_text_and_one_revision(string culture)
    {
        var catalog = BusinessFormCatalog.CreateProposedFilters(culture, Text(culture, "Supervisors"), Text(culture, "Active staff"), "{}", ProposalId, "{\"evidence\":true}");
        Assert.False(catalog.IsActive);
        Assert.Equal(1, catalog.Revision);
        Assert.Equal(BusinessFormCatalogKind.Filters, catalog.Kind);
        Assert.Equal(ProposalId, catalog.ProposalId);
        Assert.Equal(BusinessFormCatalogProposalState.Pending, catalog.ProposalState);
        Assert.Equal("{\"evidence\":true}", catalog.ProposalEvidenceJson);
        Assert.DoesNotContain(culture == "es" ? "\"en\"" : "\"es\"", catalog.NameJson, StringComparison.Ordinal);
        Assert.Null(catalog.ProposalDecidedByUserId);
        Assert.Null(catalog.ProposalDecidedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_pending_proposal_cannot_be_activated_by_an_ordinary_update(bool update)
    {
        var catalog = New();
        var result = update ? catalog.UpdateFilters(1, Text("es", "Changed"), Text("es", "Changed"), "{}", true) : catalog.SetActive(1, true);
        Assert.True(result.IsFailure);
        Assert.False(catalog.IsActive);
        Assert.Equal(1, catalog.Revision);
        Assert.Contains("Supervisores", catalog.NameJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Approval_saves_the_edited_filters_and_decision_in_one_revision()
    {
        var catalog = New();
        var evidence = catalog.ProposalEvidenceJson;
        var result = catalog.ApproveProposal(1, ActorId, Now, Text("es", "Responsables"), Text("es", "Editada"), "{\"excludeRecipient\":true}");
        Assert.True(result.IsSuccess);
        Assert.True(catalog.IsActive);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal(BusinessFormCatalogProposalState.Approved, catalog.ProposalState);
        Assert.Equal(ActorId, catalog.ProposalDecidedByUserId);
        Assert.Equal(Now, catalog.ProposalDecidedAtUtc);
        Assert.Contains("Responsables", catalog.NameJson, StringComparison.Ordinal);
        Assert.Equal("{\"excludeRecipient\":true}", catalog.FiltersJson);
        Assert.Equal(evidence, catalog.ProposalEvidenceJson);
        Assert.Equal(ProposalId, catalog.ProposalId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_discarded_proposal_stays_inactive_and_cannot_be_activated(bool update)
    {
        var catalog = New();
        Assert.True(catalog.DiscardProposal(1, ActorId, Now).IsSuccess);
        Assert.Equal(BusinessFormCatalogProposalState.Discarded, catalog.ProposalState);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal(ActorId, catalog.ProposalDecidedByUserId);
        Assert.Equal(Now, catalog.ProposalDecidedAtUtc);
        var result = update ? catalog.UpdateFilters(2, Text("es", "Changed"), Text("es", "Changed"), "{}", true) : catalog.SetActive(2, true);
        Assert.True(result.IsFailure);
        Assert.False(catalog.IsActive);
        Assert.Equal(2, catalog.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stale_decision_preserves_the_original_proposal(bool approve)
    {
        var catalog = New();
        var result = approve ? catalog.ApproveProposal(0, ActorId, Now, Text("es", "Wrong"), Text("es", "Wrong"), "{\"changed\":true}") : catalog.DiscardProposal(0, ActorId, Now);
        Assert.Equal(FormCatalogErrors.RevisionConflict, result.Error);
        Assert.Equal(BusinessFormCatalogProposalState.Pending, catalog.ProposalState);
        Assert.Equal(1, catalog.Revision);
        Assert.False(catalog.IsActive);
        Assert.Null(catalog.ProposalDecidedAtUtc);
    }

    [Fact]
    public void Later_catalog_edits_preserve_the_approval_and_original_evidence()
    {
        var catalog = New();
        Assert.True(catalog.ApproveProposal(1, ActorId, Now, Text("es", "Supervisores"), Text("es", "Personas activas"), "{}").IsSuccess);
        var evidence = catalog.ProposalEvidenceJson;
        Assert.True(catalog.UpdateFilters(2, Text("es", "Renamed"), Text("es", "Edited"), "{\"later\":true}", false).IsSuccess);
        Assert.False(catalog.IsActive);
        Assert.Equal(3, catalog.Revision);
        Assert.Equal(BusinessFormCatalogProposalState.Approved, catalog.ProposalState);
        Assert.Equal(evidence, catalog.ProposalEvidenceJson);
        Assert.Equal(ActorId, catalog.ProposalDecidedByUserId);
        Assert.Equal(Now, catalog.ProposalDecidedAtUtc);
    }

    [Fact]
    public void A_manual_proposal_starts_inactive_and_pending_with_its_value_type()
    {
        var catalog = BusinessFormCatalog.CreateProposedManual("en", Text("en", "Carriers"), Text("en", "Companies"), BusinessFormCatalogValueType.Integer,
            ProposalId, "{\"manual\":true}");
        Assert.Equal((BusinessFormCatalogKind.Manual, false, 1L), (catalog.Kind, catalog.IsActive, catalog.Revision));
        Assert.Equal((BusinessFormCatalogValueType.Integer, "en"), (catalog.ValueType, catalog.OriginalCulture));
        Assert.Equal((ProposalId, BusinessFormCatalogProposalState.Pending, "{\"manual\":true}"), (catalog.ProposalId!.Value, catalog.ProposalState!.Value, catalog.ProposalEvidenceJson));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_manual_proposal_is_edited_like_any_manual_catalog_but_not_activated_by_it(bool update)
    {
        var catalog = BusinessFormCatalog.CreateProposedManual("es", Text("es", "Transportistas"), Text("es", "Empresas"), BusinessFormCatalogValueType.Text,
            ProposalId, "{}");
        Assert.Equal(FormCatalogErrors.Inactive, update ? catalog.UpdateManual(1, Text("es", "Otro"), Text("es", "Otra"), true).Error : catalog.SetActive(1, true).Error);
        Assert.True(catalog.UpdateManual(1, Text("es", "Transportes"), Text("es", "Empresas"), false).IsSuccess);
        Assert.Equal((false, 2L, BusinessFormCatalogProposalState.Pending), (catalog.IsActive, catalog.Revision, catalog.ProposalState!.Value));
    }

    [Fact]
    public void Approving_a_manual_proposal_only_activates_it_and_keeps_the_decision()
    {
        var catalog = BusinessFormCatalog.CreateProposedManual("es", Text("es", "Transportistas"), Text("es", "Empresas"), BusinessFormCatalogValueType.Text,
            ProposalId, "{}");
        Assert.Equal(FormCatalogErrors.RevisionConflict, catalog.ApproveManualProposal(2, ActorId, Now).Error);
        Assert.True(catalog.ApproveManualProposal(1, ActorId, Now).IsSuccess);
        Assert.Equal((true, 2L, BusinessFormCatalogProposalState.Approved), (catalog.IsActive, catalog.Revision, catalog.ProposalState!.Value));
        Assert.Equal((ActorId, Now), (catalog.ProposalDecidedByUserId!.Value, catalog.ProposalDecidedAtUtc!.Value));
        Assert.Contains("Transportistas", catalog.NameJson, StringComparison.Ordinal);
        Assert.Equal(FormCatalogErrors.RevisionConflict, catalog.ApproveManualProposal(2, ActorId, Now).Error);
    }

    [Fact]
    public void A_filters_proposal_is_not_approved_as_a_manual_one()
    {
        var catalog = New();
        Assert.Equal(FormCatalogErrors.RevisionConflict, catalog.ApproveManualProposal(1, ActorId, Now).Error);
        Assert.Equal((false, BusinessFormCatalogProposalState.Pending), (catalog.IsActive, catalog.ProposalState!.Value));
    }

    [Fact]
    public void A_normal_filters_catalog_keeps_its_existing_creation_behavior()
    {
        var catalog = BusinessFormCatalog.CreateFilters("es", Text("es", "Normal"), Text("es", "Normal"), "{}");
        Assert.True(catalog.IsActive);
        Assert.Equal(1, catalog.Revision);
        Assert.Null(catalog.ProposalState);
        Assert.Null(catalog.ProposalId);
        Assert.True(catalog.UpdateFilters(1, Text("es", "Normal"), Text("es", "Normal"), "{}", false).IsSuccess);
        Assert.True(catalog.SetActive(2, true).IsSuccess);
    }
}
