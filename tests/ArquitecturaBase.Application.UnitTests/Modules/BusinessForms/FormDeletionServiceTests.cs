using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormDeletionServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData("anonymous")]
    [InlineData("read")]
    [InlineData("manage")]
    [InlineData("publish")]
    public async Task Deletion_requires_read_and_manage_before_validation_or_transaction(string access)
    {
        var f = Fixture();
        if (access == "anonymous") f.User.UserId = null;
        else f.Permissions.Permissions[f.User.UserId!.Value] = access == "read" ? [BusinessFormsPermissions.Read]
            : access == "manage" ? [BusinessFormsPermissions.Manage] : [BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish];
        var result = await f.DeleteAsync(f.Definition.Id, 0, Ct);
        Assert.Equal(access == "anonymous" ? FormDefinitionErrors.Unauthorized.Code : FormDefinitionErrors.Forbidden.Code, result.Error.Code);
        Assert.Equal(0, f.UnitOfWork.Transactions); Assert.Equal(0, f.StateReads); Assert.Empty(f.Removed);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9007199254740992)]
    public async Task Invalid_revisions_are_rejected_before_the_delete_boundary(long revision)
    {
        var f = Fixture(); var result = await f.DeleteAsync(f.Definition.Id, revision, Ct);
        Assert.IsType<ValidationError>(result.Error); Assert.Equal(0, f.UnitOfWork.Transactions); Assert.Empty(f.Removed);
    }
    [Theory]
    [InlineData("draft")]
    [InlineData("published")]
    [InlineData("deactivated")]
    public async Task An_empty_definition_can_be_removed_once_without_publish_or_records_permission(string state)
    {
        var f = Fixture(state != "draft");
        if (state == "deactivated") Assert.True(f.Definition.Deactivate(1, f.User.UserId!.Value, f.Clock.GetUtcNow().UtcDateTime).IsSuccess);
        var revision = f.Definition.Revision; var version = f.Definition.ActiveVersionId;
        var result = await f.DeleteAsync(f.Definition.Id, revision, Ct);
        Assert.True(result.IsSuccess); Assert.Same(f.Definition, Assert.Single(f.Removed));
        Assert.Equal(1, f.UnitOfWork.Transactions); Assert.Equal(1, f.UnitOfWork.Commits); Assert.Equal(CommitPolicy.OnSuccess, f.UnitOfWork.LastPolicy);
        Assert.Equal(revision, f.Definition.Revision); Assert.Equal(version, f.Definition.ActiveVersionId); Assert.Equal(0, f.Definition.LastRecordNumber);
    }
    [Theory]
    [InlineData("revision")]
    [InlineData("missing")]
    [InlineData("publication")]
    [InlineData("records")]
    [InlineData("session")]
    [InlineData("pendingReply")]
    public async Task A_state_conflict_never_removes_the_definition(string conflict)
    {
        var f = Fixture(); f.PublicationOpen = conflict == "publication";
        if (conflict == "records") f.Definition.AllocateRecordNumber();
        if (conflict == "session") f.LastOpenSessionExpiresAtUtc = f.Clock.GetUtcNow().UtcDateTime.AddHours(1);
        if (conflict == "pendingReply") f.HasPendingReplies = true;
        var result = await f.DeleteAsync(conflict == "missing" ? Guid.NewGuid() : f.Definition.Id, conflict == "revision" ? 99 : 1, Ct);
        Assert.True(result.IsFailure); Assert.Empty(f.Removed); Assert.Equal(1, f.UnitOfWork.Rollbacks);
        if (conflict == "revision") Assert.Equal(FormDraftErrors.RevisionConflict.Code, result.Error.Code);
        if (conflict == "missing") Assert.Equal(FormDefinitionErrors.NotFound.Code, result.Error.Code);
        if (conflict == "publication") Assert.Equal(FormPublicationErrors.InProgress.Code, result.Error.Code);
        Assert.Equal(1, f.Definition.Revision);
    }
    [Fact]
    public async Task Deletion_rechecks_permissions_after_waiting_for_the_definition_lock()
    {
        var f = Fixture(); f.BeforeLock = () => f.Permissions.Permissions[f.User.UserId!.Value] = [];
        var result = await f.DeleteAsync(f.Definition.Id, 1, Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, result.Error.Code); Assert.Equal(0, f.EntityReads); Assert.Empty(f.Removed);
    }
    [Theory]
    [InlineData("draft")]
    [InlineData("record")]
    public async Task An_edit_or_capture_that_wins_the_lock_is_observed_by_delete(string winner)
    {
        var f = Fixture();
        f.BeforeLock = () =>
        {
            if (winner == "record") f.Definition.AllocateRecordNumber();
            else Assert.True(f.Definition.SetDraft(1, "Other edit", "{\"es\":\"Other edit\"}", "FORM", "es",
                "{\"es\":\"Enviar\"}", "{\"format\":1,\"fields\":[]}", 0, false).IsSuccess);
        };
        var result = await f.DeleteAsync(f.Definition.Id, 1, Ct);
        Assert.True(result.IsFailure); Assert.Empty(f.Removed); Assert.Equal(1, f.UnitOfWork.Rollbacks);
    }
    [Fact]
    public async Task A_channel_observation_failure_never_turns_into_permission_to_delete()
    {
        var f = Fixture(); f.StateReadFailure = new InvalidOperationException("State unavailable");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.DeleteAsync(f.Definition.Id, 1, Ct));
        Assert.Empty(f.Removed); Assert.Equal(1, f.UnitOfWork.Rollbacks);
    }
    private static FormLifecycleFixture Fixture(bool published = true)
    {
        var fixture = new FormLifecycleFixture(published);
        fixture.Permissions.Permissions[fixture.User.UserId!.Value] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage];
        return fixture;
    }
}
