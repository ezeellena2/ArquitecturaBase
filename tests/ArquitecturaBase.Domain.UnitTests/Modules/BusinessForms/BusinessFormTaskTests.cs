using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormTaskTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_decision_closes_the_task_and_keeps_its_received_rule_revision(bool approved)
    {
        var task = Open();
        var rules = task.RuleSetId;
        var actor = Guid.NewGuid();
        Assert.True(task.Decide(1, actor, approved, approved ? null : " Revisá la visita ", Now).IsSuccess);
        Assert.Equal(approved ? FormTaskState.Approved : FormTaskState.Rejected, task.State);
        Assert.Equal(2, task.Revision);
        Assert.Equal(rules, task.RuleSetId);
        Assert.Equal(actor, task.DecidedByUserId);
        Assert.Equal(Now, task.DecidedAtUtc);
        Assert.Equal(approved ? null : "Revisá la visita", task.Reason);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_confirmed_author_cannot_decide_their_own_response(bool approved)
    {
        var task = Open();
        Assert.Equal(FormApprovalErrors.OwnResponse, task.Decide(1, task.AuthorUserId, approved, approved ? null : "Motivo", Now).Error);
        Assert.Equal(FormTaskState.Open, task.State);
        Assert.Equal(1, task.Revision);
    }
    [Fact]
    public void A_stale_or_second_decision_conflicts_without_changing_the_first()
    {
        var task = Open();
        var actor = Guid.NewGuid();
        Assert.Equal(FormApprovalErrors.RevisionConflict, task.Decide(2, actor, true, null, Now).Error);
        Assert.True(task.Decide(1, actor, true, null, Now).IsSuccess);
        Assert.Equal(FormApprovalErrors.RevisionConflict, task.Decide(1, Guid.NewGuid(), false, "Otro motivo", Now).Error);
        Assert.Equal(FormTaskState.Approved, task.State);
        Assert.Equal(actor, task.DecidedByUserId);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejection_requires_a_visible_reason(string? reason) =>
        Assert.Equal(FormApprovalErrors.ReasonRequired, Open().Decide(1, Guid.NewGuid(), false, reason, Now).Error);
    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Rejection_keeps_a_reason_of_at_most_five_hundred_characters(int length, bool valid) =>
        Assert.Equal(valid, Open().Decide(1, Guid.NewGuid(), false, new string('a', length), Now).IsSuccess);
    [Fact]
    public void Approval_does_not_accept_a_rejection_reason_and_reason_text_must_be_storable()
    {
        Assert.True(Open().Decide(1, Guid.NewGuid(), true, "Motivo", Now).IsFailure);
        Assert.True(Open().Decide(1, Guid.NewGuid(), false, new string((char)0, 1), Now).IsFailure);
    }
    private static BusinessFormTask Open() => BusinessFormTask.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
}
