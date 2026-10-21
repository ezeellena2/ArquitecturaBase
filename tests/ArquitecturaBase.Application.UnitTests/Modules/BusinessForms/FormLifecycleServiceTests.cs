using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormLifecycleServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData("anonymous")]
    [InlineData("read")]
    [InlineData("publish")]
    [InlineData("manage")]
    public async Task Deactivation_and_its_preview_require_read_and_publish_without_opening_a_transaction_on_denial(string access)
    {
        var f = new FormLifecycleFixture();
        if (access == "anonymous") f.User.UserId = null;
        else f.Permissions.Permissions[f.User.UserId!.Value] = access == "read" ? [BusinessFormsPermissions.Read]
            : access == "publish" ? [BusinessFormsPermissions.Publish] : [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage];
        var preview = await f.Service.GetDeactivationAsync(f.Definition.Id, Ct);
        var result = await f.Service.DeactivateAsync(f.Definition.Id, new(1), Ct);
        Assert.Equal(access == "anonymous" ? FormDefinitionErrors.Unauthorized.Code : FormDefinitionErrors.Forbidden.Code, result.Error.Code);
        Assert.True(preview.IsFailure); Assert.Equal(0, f.UnitOfWork.Transactions); Assert.Equal(0, f.BatchReads);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_preview_exposes_only_metadata_and_hides_all_record_counters_without_record_read(bool recordsRead)
    {
        var f = new FormLifecycleFixture();
        if (!recordsRead) f.Permissions.Permissions[f.User.UserId!.Value] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish];
        for (var i = 0; i < 4; i++) f.Definition.AllocateRecordNumber();
        f.Incoming = [new(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = "Origen" })];
        var result = await f.Service.GetDeactivationAsync(f.Definition.Id, Ct);
        Assert.True(result.IsSuccess); Assert.Equal(f.Incoming, result.Value.AffectedSuggestions);
        Assert.Equal(recordsRead ? 4 : (long?)null, result.Value.Lifecycle.ResponseCount);
        Assert.Equal(recordsRead ? 2 : (int?)null, result.Value.Lifecycle.PendingApprovalCount);
        Assert.Equal(recordsRead ? "FORM-000005" : null, result.Value.Lifecycle.NextRecordNumber);
        Assert.Equal(2, result.Value.Lifecycle.NextVersionNumber); Assert.True(result.Value.Lifecycle.CanDeactivate); Assert.False(result.Value.Lifecycle.CanReactivate);
        Assert.Equal(0, f.UnitOfWork.Transactions); Assert.Equal(1, f.BatchReads);
    }
    [Fact]
    public async Task A_publisher_without_manage_deactivates_under_one_lock_and_one_commit()
    {
        var f = new FormLifecycleFixture(); var version = f.Definition.ActiveVersionId;
        var result = await f.Service.DeactivateAsync(f.Definition.Id, new(1), Ct);
        Assert.True(result.IsSuccess); Assert.Equal(2, result.Value.Revision);
        Assert.Equal(f.Clock.GetUtcNow().UtcDateTime, f.Definition.DeactivatedAtUtc); Assert.Equal(f.User.UserId, f.Definition.DeactivatedBy);
        Assert.Equal(version, f.Definition.ActiveVersionId); Assert.Equal(1, f.UnitOfWork.Transactions); Assert.Equal(1, f.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, f.UnitOfWork.LastPolicy); Assert.False(result.Value.Lifecycle.CanDeactivate); Assert.True(result.Value.Lifecycle.CanReactivate);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_write_receipt_never_adopts_a_later_edits_revision_or_deactivation_author(bool reactivated)
    {
        var f = new FormLifecycleFixture(); var actor = f.User.UserId!.Value; var at = f.Clock.GetUtcNow().UtcDateTime;
        f.BeforeBatchRead = () =>
        {
            Assert.False(f.UnitOfWork.InTransaction);
            if (reactivated)
            {
                Assert.True(f.Definition.ActivateVersion(f.Definition.ActiveVersionId, Guid.NewGuid()).IsSuccess);
                Assert.True(f.Definition.Deactivate(2, Guid.NewGuid(), at.AddHours(1)).IsSuccess);
            }
            else Assert.True(f.Definition.SetDraft(2, "Other edit", "{\"es\":\"Other edit\"}", "FORM", "es",
                "{\"es\":\"Enviar\"}", "{\"format\":1,\"fields\":[]}", 0, false).IsSuccess);
        };
        var result = await f.Service.DeactivateAsync(f.Definition.Id, new(1), Ct);
        Assert.True(result.IsSuccess); Assert.Equal(3, f.Definition.Revision);
        Assert.Equal(2, result.Value.Revision); Assert.Equal(2, result.Value.Lifecycle.Revision);
        Assert.Equal(at, result.Value.Lifecycle.DeactivatedAtUtc); Assert.Equal(actor, result.Value.Lifecycle.DeactivatedBy?.UserId);
        Assert.Equal(1, f.UnitOfWork.Commits);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9007199254740992)]
    public async Task An_invalid_revision_is_rejected_before_the_write_boundary(long revision)
    {
        var f = new FormLifecycleFixture(); var result = await f.Service.DeactivateAsync(f.Definition.Id, new(revision), Ct);
        Assert.True(result.IsFailure); Assert.Equal(0, f.UnitOfWork.Transactions);
    }
    [Theory]
    [InlineData("revision")]
    [InlineData("publication")]
    [InlineData("draft")]
    [InlineData("missing")]
    public async Task State_conflicts_and_missing_definitions_never_change_the_definition(string conflict)
    {
        var f = new FormLifecycleFixture(conflict != "draft") { PublicationOpen = conflict == "publication" };
        var result = await f.Service.DeactivateAsync(conflict == "missing" ? Guid.NewGuid() : f.Definition.Id, new(conflict == "revision" ? 9 : 1), Ct);
        Assert.True(result.IsFailure); Assert.Equal(1, f.Definition.Revision); Assert.Null(f.Definition.DeactivatedAtUtc);
        Assert.Equal(1, f.UnitOfWork.Rollbacks);
        Assert.Equal(conflict == "publication" ? FormPublicationErrors.InProgress.Code : conflict == "revision" ? FormDraftErrors.RevisionConflict.Code
            : conflict == "missing" ? FormDefinitionErrors.NotFound.Code : FormDefinitionErrors.DeactivationUnavailable.Code, result.Error.Code);
    }
    [Fact]
    public async Task Permissions_are_revalidated_after_waiting_for_the_definition_lock()
    {
        var f = new FormLifecycleFixture(); f.BeforeLock = () => f.Permissions.Permissions[f.User.UserId!.Value] = [];
        var result = await f.Service.DeactivateAsync(f.Definition.Id, new(1), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, result.Error.Code); Assert.Equal(0, f.EntityReads); Assert.Null(f.Definition.DeactivatedAtUtc);
    }
    [Fact]
    public async Task A_revocation_during_the_preview_discards_the_whole_result()
    {
        var f = new FormLifecycleFixture(); f.BeforeBatchReturn = () => f.Permissions.Permissions[f.User.UserId!.Value] = [];
        var result = await f.Service.GetDeactivationAsync(f.Definition.Id, Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, result.Error.Code);
    }
    [Fact]
    public async Task An_open_publication_is_an_explicit_reason_and_disables_both_actions()
    {
        var f = new FormLifecycleFixture { PublicationOpen = true };
        var result = await f.Service.GetDeactivationAsync(f.Definition.Id, Ct);
        Assert.Equal("PublicationInProgress", result.Value.Lifecycle.UnavailableReason); Assert.False(result.Value.Lifecycle.CanDeactivate); Assert.False(result.Value.Lifecycle.CanReactivate);
    }
}
