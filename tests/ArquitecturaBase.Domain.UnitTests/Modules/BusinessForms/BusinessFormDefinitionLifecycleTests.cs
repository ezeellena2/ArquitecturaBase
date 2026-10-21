using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormDefinitionLifecycleTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Deactivation_records_its_author_and_advances_revision_without_changing_published_or_draft_content()
    {
        var definition = Published();
        var active = definition.ActiveVersionId;
        var actor = Guid.NewGuid();
        var rules = Guid.NewGuid(); var pdf = Guid.NewGuid(); var suggestions = Guid.NewGuid();
        definition.SetActiveRuleSetId(rules); definition.SetActivePdfDesignId(pdf); definition.SetActiveSuggestionSetId(suggestions);
        definition.AllocateRecordNumber(); definition.AllocateRecordNumber();

        Assert.True(definition.Deactivate(1, actor, Now).IsSuccess);

        Assert.Equal(2, definition.Revision);
        Assert.Equal(Now, definition.DeactivatedAtUtc);
        Assert.Equal(actor, definition.DeactivatedBy);
        Assert.Equal(active, definition.ActiveVersionId); Assert.Equal(2, definition.LastRecordNumber);
        Assert.Equal(rules, definition.ActiveRuleSetId); Assert.Equal(pdf, definition.ActivePdfDesignId); Assert.Equal(suggestions, definition.ActiveSuggestionSetId);
        Assert.Equal("Original", definition.Name); Assert.Equal("{schema}", definition.SchemaJson); Assert.Equal("When requested", definition.OfferWhen);
        Assert.True(definition.PrefixLocked);
    }

    [Fact]
    public void Saving_a_deactivated_draft_preserves_its_deactivation_and_active_version()
    {
        var definition = Published(); var active = definition.ActiveVersionId; var actor = Guid.NewGuid();
        Assert.True(definition.Deactivate(1, actor, Now).IsSuccess);

        Assert.True(definition.SetDraft(2, "Edited", "{edited-name}", "FORM", "es", "{edited-button}", "{edited-schema}", 3, false).IsSuccess);

        Assert.Equal(3, definition.Revision); Assert.Equal("Edited", definition.Name); Assert.Equal(active, definition.ActiveVersionId);
        Assert.Equal(Now, definition.DeactivatedAtUtc); Assert.Equal(actor, definition.DeactivatedBy);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("revision")]
    [InlineData("deactivated")]
    [InlineData("actor")]
    [InlineData("utc")]
    public void An_invalid_deactivation_leaves_the_definition_unchanged(string reason)
    {
        var definition = reason == "draft" ? BusinessFormDefinition.Create() : Published();
        var actor = Guid.NewGuid();
        if (reason == "deactivated") Assert.True(definition.Deactivate(1, actor, Now).IsSuccess);
        var revision = definition.Revision;
        var at = definition.DeactivatedAtUtc;
        var by = definition.DeactivatedBy;
        var result = definition.Deactivate(reason == "revision" ? 999 : revision, reason == "actor" ? Guid.Empty : actor,
            reason == "utc" ? DateTime.SpecifyKind(Now, DateTimeKind.Unspecified) : Now);
        Assert.True(result.IsFailure); Assert.Equal(revision, definition.Revision);
        Assert.Equal(at, definition.DeactivatedAtUtc); Assert.Equal(by, definition.DeactivatedBy);
    }

    [Fact]
    public void Only_activating_a_new_version_with_the_current_base_reactivates_without_resetting_numbering()
    {
        var definition = Published(); var active = definition.ActiveVersionId; var actor = Guid.NewGuid();
        definition.AllocateRecordNumber(); definition.AllocateRecordNumber();
        Assert.True(definition.Deactivate(1, actor, Now).IsSuccess);
        Assert.True(definition.ActivateVersion(null, Guid.NewGuid()).IsFailure);
        Assert.True(definition.ActivateVersion(active, Guid.Empty).IsFailure);
        Assert.True(definition.ActivateVersion(active, active!.Value).IsFailure);
        Assert.Equal(Now, definition.DeactivatedAtUtc);
        var next = Guid.NewGuid(); Assert.True(definition.ActivateVersion(active, next).IsSuccess);
        Assert.Equal(next, definition.ActiveVersionId); Assert.Null(definition.DeactivatedAtUtc); Assert.Null(definition.DeactivatedBy);
        Assert.Equal(2, definition.Revision); Assert.Equal(3, definition.AllocateRecordNumber()); Assert.Equal("{schema}", definition.SchemaJson);
    }

    [Fact]
    public void Reactivating_the_same_published_version_is_not_an_activation()
    {
        var definition = Published();
        Assert.True(definition.ActivateVersion(definition.ActiveVersionId, definition.ActiveVersionId!.Value).IsFailure);
    }

    private static BusinessFormDefinition Published()
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Original", "{name}", "FORM", "es", "{button}", "{schema}", 2, true, "When requested").IsSuccess);
        definition.LockPrefix(); Assert.True(definition.ActivateVersion(null, Guid.NewGuid()).IsSuccess); return definition;
    }
}
