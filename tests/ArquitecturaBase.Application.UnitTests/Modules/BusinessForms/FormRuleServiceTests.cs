using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormRuleServiceTests
{
    [Fact]
    public async Task An_empty_configuration_has_revision_zero_and_needs_only_rule_management()
    {
        var fixture = new FormRuleFixture();
        var current = await fixture.Service.GetCurrentAsync(fixture.Definition.Id,TestContext.Current.CancellationToken);
        Assert.True(current.IsSuccess);
        Assert.Null(current.Value.Id);
        Assert.Equal(0,current.Value.Revision);
        Assert.Empty(current.Value.Rules);
        var options = await fixture.Service.GetOptionsAsync(fixture.Definition.Id,TestContext.Current.CancellationToken);
        Assert.True(options.IsSuccess);
        Assert.Equal(fixture.FieldId,Assert.Single(options.Value.Questions).FieldId);
        Assert.Equal(fixture.RoleId,Assert.Single(options.Value.Roles).Id);
        Assert.Equal(0,fixture.UnitOfWork.Transactions);
    }
    [Fact]
    public async Task Saving_adds_an_immutable_revision_and_moves_the_pointer_without_changing_the_draft()
    {
        var fixture = new FormRuleFixture();
        var draftRevision = fixture.Definition.Revision;
        var request = new SaveFormRulesRequest(0,[new(Guid.NewGuid(),"Received",[],[new(Guid.NewGuid(),"RequestApproval",fixture.RoleId)])]);
        var saved = await fixture.Service.SaveAsync(fixture.Definition.Id,request,TestContext.Current.CancellationToken);
        Assert.True(saved.IsSuccess);
        Assert.Equal(1,saved.Value.Revision);
        Assert.Equal(saved.Value.Id,fixture.Definition.ActiveRuleSetId);
        Assert.Equal(draftRevision,fixture.Definition.Revision);
        Assert.Equal(CommitPolicy.OnSuccess,fixture.UnitOfWork.LastPolicy);
        Assert.Equal(1,fixture.UnitOfWork.Commits);
        var first = Assert.Single(fixture.RuleSets);
        Assert.True(first.HasApproval);
        Assert.Equal(fixture.FieldId,Assert.Single(FormDraftPolicy.Read<FormVersionSchema>(first.QuestionSchemaJson).Questions).FieldId);
        var disabled = await fixture.Service.SaveAsync(fixture.Definition.Id,new(1,[]),TestContext.Current.CancellationToken);
        Assert.True(disabled.IsSuccess);
        Assert.Equal(2,disabled.Value.Revision);
        Assert.False(fixture.RuleSets[^1].HasApproval);
        Assert.NotEqual(first.Id,fixture.Definition.ActiveRuleSetId);
        Assert.Contains("RequestApproval",first.RulesJson,StringComparison.Ordinal);
        var history = await fixture.Service.ListRevisionsAsync(fixture.Definition.Id,new(),TestContext.Current.CancellationToken);
        Assert.Equal(new long[] { 2,1 },history.Value.Items.Select(row=>row.Revision));
    }
    [Fact]
    public async Task A_stale_revision_is_a_conflict_and_adds_no_revision()
    {
        var fixture = new FormRuleFixture();
        Assert.True((await fixture.Service.SaveAsync(fixture.Definition.Id,new(0,[]),TestContext.Current.CancellationToken)).IsSuccess);
        var stale = await fixture.Service.SaveAsync(fixture.Definition.Id,new(0,[]),TestContext.Current.CancellationToken);
        Assert.Equal(FormRuleErrors.RevisionConflict,stale.Error);
        Assert.Single(fixture.RuleSets);
        Assert.Equal(1,fixture.UnitOfWork.Rollbacks);
    }
    [Fact]
    public async Task Revocation_while_waiting_for_the_write_lock_prevents_the_revision()
    {
        var fixture = new FormRuleFixture();
        fixture.BeforeLock = () => fixture.Permissions.Permissions.Clear();
        var result = await fixture.Service.SaveAsync(fixture.Definition.Id,new(0,[]),TestContext.Current.CancellationToken);
        Assert.Equal(FormDefinitionErrors.Forbidden,result.Error);
        Assert.Empty(fixture.RuleSets);
        Assert.Null(fixture.Definition.ActiveRuleSetId);
    }
    [Fact]
    public async Task Revocation_during_read_discards_the_rules()
    {
        var fixture = new FormRuleFixture();
        fixture.BeforeCurrentReturn = () => fixture.Permissions.Permissions.Clear();
        Assert.Equal(FormDefinitionErrors.Forbidden,(await fixture.Service.GetCurrentAsync(fixture.Definition.Id,TestContext.Current.CancellationToken)).Error);
    }
}
