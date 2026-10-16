using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormRuleSelectionTests
{
    [Fact]
    public void Rules_have_their_own_permission_in_the_module_catalog()
    {
        Assert.Contains("formrules.manage", BusinessFormsPermissions.All);
    }

    [Fact]
    public void Selecting_rules_keeps_the_draft_revision_and_published_version()
    {
        var definition = BusinessFormDefinition.Create();
        var versionId = Guid.NewGuid();
        Assert.True(definition.ActivateVersion(null, versionId).IsSuccess);
        var revision = definition.Revision;
        var ruleSetId = Guid.NewGuid();
        definition.SetActiveRuleSetId(ruleSetId);
        Assert.Equal(ruleSetId, definition.ActiveRuleSetId);
        Assert.Equal(revision, definition.Revision);
        Assert.Equal(versionId, definition.ActiveVersionId);
    }
}
