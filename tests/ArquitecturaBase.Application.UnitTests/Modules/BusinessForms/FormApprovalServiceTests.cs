using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormApprovalServiceTests
{
    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public async Task A_decision_closes_record_and_task_and_records_the_captured_rule_revision_in_one_commit(string decision)
    {
        var fixture = new FormApprovalFixture();
        var newer = BusinessFormRuleSet.Create(fixture.Record.DefinitionId, 2, "es", "[]", false, FormDraftPolicy.Serialize(fixture.Actions.Schema));
        fixture.Actions.Rules.RuleSets.Add(newer); fixture.Actions.Rules.Definition.SetActiveRuleSetId(newer.Id);
        var result = await fixture.Service.DecideAsync(fixture.Task.Id, new(1, decision, decision == "Rejected" ? "Revisá el dato" : null), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(decision, fixture.Task.State.ToString());
        Assert.Equal(decision, fixture.Record.State.ToString());
        var run = Assert.Single(fixture.Actions.Runs.Runs);
        Assert.Equal(fixture.Task.RuleSetId, run.RuleSetId);
        Assert.Equal(decision, run.Event);
        Assert.Equal(1, fixture.UnitOfWork.Transactions);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);
        Assert.Equal(2, result.Value.Revision);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Permission_or_role_revoked_while_waiting_for_the_lock_cannot_decide(bool permission)
    {
        var fixture = new FormApprovalFixture();
        fixture.BeforeLock = () => { if (permission) fixture.Permissions.Permissions.Clear(); else fixture.IsInRole = false; };
        var result = await fixture.Service.DecideAsync(fixture.Task.Id, new(1, "Approved"), TestContext.Current.CancellationToken);
        Assert.Equal(permission ? FormDefinitionErrors.Forbidden : FormApprovalErrors.RoleChanged, result.Error);
        Assert.Equal(FormTaskState.Open, fixture.Task.State);
        Assert.Equal(FormRecordState.PendingApproval, fixture.Record.State);
        Assert.Empty(fixture.Actions.Runs.Runs);
    }
    [Fact]
    public async Task Own_response_and_a_second_decision_never_record_another_event()
    {
        var own = new FormApprovalFixture(); own.User.UserId = own.Task.AuthorUserId; own.Permissions.Permissions[own.User.UserId.Value] = [BusinessFormsPermissions.ApprovalsDecide];
        Assert.Equal(FormApprovalErrors.OwnResponse, (await own.Service.DecideAsync(own.Task.Id, new(1, "Approved"), TestContext.Current.CancellationToken)).Error);
        Assert.Empty(own.Actions.Runs.Runs);
        var fixture = new FormApprovalFixture();
        Assert.True((await fixture.Service.DecideAsync(fixture.Task.Id, new(1, "Approved"), TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(FormApprovalErrors.RevisionConflict, (await fixture.Service.DecideAsync(fixture.Task.Id, new(1, "Rejected", "Otro motivo"), TestContext.Current.CancellationToken)).Error);
        Assert.Single(fixture.Actions.Runs.Runs);
    }
    // RC4-A: «Puede decidir quien cargó» lets whoever loaded the response, corrected it or passed it from a document decide it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_approval_whose_author_may_decide_lets_whoever_set_the_values_decide(bool loadedIt)
    {
        var fixture = new FormApprovalFixture(authorMayDecide: true);
        if (loadedIt) { fixture.User.UserId = fixture.Task.AuthorUserId; fixture.Permissions.Permissions[fixture.User.UserId.Value] = [BusinessFormsPermissions.ApprovalsDecide]; }
        else fixture.ValuesSetByActor = true;
        var result = await fixture.Service.DecideAsync(fixture.Task.Id, new(1, "Approved"), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(FormTaskState.Approved, fixture.Task.State);
        Assert.Single(fixture.Actions.Runs.Runs);
    }
    [Fact]
    public async Task Whoever_corrected_the_response_cannot_decide_it_when_the_approval_keeps_the_author_out()
    {
        var fixture = new FormApprovalFixture { ValuesSetByActor = true };
        Assert.Equal(FormApprovalErrors.OwnResponse, (await fixture.Service.DecideAsync(fixture.Task.Id, new(1, "Approved"), TestContext.Current.CancellationToken)).Error);
        Assert.Equal(FormTaskState.Open, fixture.Task.State);
        Assert.Empty(fixture.Actions.Runs.Runs);
    }
    // RC4-A: without «Exigir motivo al rechazar», a rejection goes without a reason; with it, the missing reason is refused on its field.
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_rejection_whose_reason_is_optional_goes_without_one(string? reason)
    {
        var fixture = new FormApprovalFixture(reasonOptional: true);
        var result = await fixture.Service.DecideAsync(fixture.Task.Id, new(1, "Rejected", reason), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Reason);
        Assert.Equal(FormTaskState.Rejected, fixture.Task.State);
        Assert.Equal("Rejected", Assert.Single(fixture.Actions.Runs.Runs).Event);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_rejection_without_a_reason_is_refused_on_the_reason_when_the_approval_asks_for_one(string? reason)
    {
        var fixture = new FormApprovalFixture();
        var result = await fixture.Service.DecideAsync(fixture.Task.Id, new(1, "Rejected", reason), TestContext.Current.CancellationToken);
        var error = Assert.IsType<ArquitecturaBase.Domain.Results.ValidationError>(result.Error);
        Assert.Equal(FormApprovalErrors.ReasonRequired.Code, error.Code);
        Assert.Equal(["reason"], error.Errors.Keys);
        Assert.Equal(FormTaskState.Open, fixture.Task.State);
        Assert.Empty(fixture.Actions.Runs.Runs);
    }
    [Theory]
    [InlineData("list")]
    [InlineData("count")]
    [InlineData("detail")]
    public async Task Revocation_during_read_discards_approval_metadata(string operation)
    {
        var fixture = new FormApprovalFixture(); fixture.BeforeReadReturn = () => fixture.Permissions.Permissions.Clear();
        var error = operation switch
        {
            "list" => (await fixture.Service.ListAsync(new(), TestContext.Current.CancellationToken)).Error,
            "count" => (await fixture.Service.CountAsync(new(), TestContext.Current.CancellationToken)).Error,
            _ => (await fixture.Service.GetAsync(fixture.Task.Id, TestContext.Current.CancellationToken)).Error,
        };
        Assert.Equal(FormDefinitionErrors.Forbidden, error);
    }
}
