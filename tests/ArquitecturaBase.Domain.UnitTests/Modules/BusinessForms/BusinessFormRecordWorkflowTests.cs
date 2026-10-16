using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormRecordWorkflowTests
{
    [Fact]
    public void A_response_starts_received_and_keeps_its_rule_revision()
    {
        var ruleSetId = Guid.NewGuid();
        var record = BusinessFormRecord.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc), "[]", ruleSetId);
        Assert.Equal(ruleSetId, record.RuleSetId);
        Assert.Equal(FormRecordState.Received, record.State);
    }

    [Theory]
    [InlineData(true, FormRecordState.Approved)]
    [InlineData(false, FormRecordState.Rejected)]
    public void Only_a_pending_response_can_finish_approval_once(bool approved, FormRecordState expected)
    {
        var record = BusinessFormRecord.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc), "[]");
        Assert.Equal(FormRecordErrors.StateConflict, record.CompleteApproval(approved).Error);
        Assert.Equal(FormRecordState.Received, record.State);
        Assert.True(record.MarkPendingApproval().IsSuccess);
        Assert.True(record.CompleteApproval(approved).IsSuccess);
        Assert.Equal(expected, record.State);
        Assert.Equal(FormRecordErrors.StateConflict, record.CompleteApproval(!approved).Error);
        Assert.Equal(expected, record.State);
    }

    [Fact]
    public void Only_a_received_response_can_become_pending_approval()
    {
        var record = BusinessFormRecord.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc), "[]");
        Assert.True(record.MarkPendingApproval().IsSuccess);
        var repeated = record.MarkPendingApproval();
        Assert.True(repeated.IsFailure);
        Assert.Equal("Forms.Record.StateConflict", repeated.Error.Code);
    }
}
