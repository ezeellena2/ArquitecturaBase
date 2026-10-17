using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormOfferWhenTests
{
    [Fact]
    public void Saving_offer_instructions_uses_the_draft_revision_without_changing_the_active_snapshot()
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Visit", "{}", "VISIT", "es", "{}", "{\"format\":1,\"fields\":[]}", 0, true).IsSuccess);
        var versionId = Guid.CreateVersion7();
        Assert.True(definition.ActivateVersion(null, versionId).IsSuccess);
        definition.LockPrefix();
        var schema = definition.SchemaJson;

        Assert.True(definition.SetDraft(1, "Visit", "{}", "VISIT", "es", "{}", schema, 0, false, "Cuando cuenta una visita.").IsSuccess);

        Assert.Equal("Cuando cuenta una visita.", definition.OfferWhen);
        Assert.Equal(2, definition.Revision);
        Assert.Equal(versionId, definition.ActiveVersionId);
        Assert.Equal(schema, definition.SchemaJson);
        Assert.True(definition.PrefixLocked);
    }

    [Fact]
    public void A_stale_revision_does_not_change_the_offer_instructions()
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Visit", "{}", "VISIT", "es", "{}", "{}", 0, true, "Original").IsSuccess);

        var result = definition.SetDraft(2, "Changed", "{}", "VISIT", "es", "{}", "{}", 0, false, "Changed");

        Assert.Equal(FormDraftErrors.RevisionConflict, result.Error);
        Assert.Equal("Original", definition.OfferWhen);
        Assert.Equal("Visit", definition.Name);
    }

    [Fact]
    public void Clearing_offer_instructions_disables_it_without_changing_publication()
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Visit", "{}", "VISIT", "es", "{}", "{}", 0, true, "Original").IsSuccess);
        Assert.Equal("Original", definition.OfferWhen);
        Assert.True(definition.SetDraft(1, "Visit", "{}", "VISIT", "es", "{}", "{}", 0, false, null).IsSuccess);
        Assert.Null(definition.OfferWhen);
        Assert.Equal(2, definition.Revision);
    }
}
