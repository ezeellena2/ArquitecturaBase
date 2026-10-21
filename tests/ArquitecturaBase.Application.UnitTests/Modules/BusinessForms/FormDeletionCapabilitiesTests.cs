using System.Text.Json;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormDeletionCapabilitiesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public void Existing_responses_keep_delete_hidden_even_while_a_publication_is_open()
    {
        var row = new FormLifecycleStoredRow(Guid.NewGuid(), 1, "VIS", Guid.NewGuid(), 2, 1, 0, null, null, true);
        var value = FormDefinitionLifecyclePolicy.View(row, null,
            new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc), true, true, false);
        Assert.False(value.CanDelete);
        Assert.Equal("HasResponses", value.DeletionUnavailableReason);
        Assert.Null(value.ResponseCount);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_capabilities_do_not_require_record_read_or_expose_its_counters(bool detail)
    {
        var f = new FormDefinitionLifecycleReadFixture { FirstRecordNumber = 0 };
        f.Permissions.Permissions[f.User.UserId!.Value] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage];
        var value = detail ? JsonSerializer.SerializeToElement((await f.Policy.FindAsync(f.Id, f.User.UserId.Value, Ct)).Value.Lifecycle, JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToElement((await f.Policy.ListAsync(new(), f.User.UserId.Value, Ct)).Value.Items[0].Lifecycle, JsonSerializerOptions.Web);
        Assert.True(value.TryGetProperty("canDelete", out var canDelete)); Assert.True(canDelete.GetBoolean());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("responseCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, value.GetProperty("pendingApprovalCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, value.GetProperty("nextRecordNumber").ValueKind);
        Assert.Equal(1, f.StateReads);
    }
    [Theory]
    [InlineData("records", "HasResponses")]
    [InlineData("session", "OpenSends")]
    [InlineData("reply", "OpenSends")]
    public async Task The_deletion_reason_uses_local_facts_without_disclosing_a_count(string blocker, string reason)
    {
        var f = new FormDefinitionLifecycleReadFixture { FirstRecordNumber = blocker == "records" ? 1 : 0,
            LastOpenSessionExpiresAtUtc = blocker == "session" ? new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc) : null,
            HasPendingReplies = blocker == "reply" };
        f.Permissions.Permissions[f.User.UserId!.Value] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage];
        var result = await f.Policy.FindAsync(f.Id, f.User.UserId.Value, Ct);
        var value = JsonSerializer.SerializeToElement(result.Value.Lifecycle, JsonSerializerOptions.Web);
        Assert.True(value.TryGetProperty("canDelete", out var canDelete)); Assert.False(canDelete.GetBoolean());
        Assert.Equal(reason, value.GetProperty("deletionUnavailableReason").GetString()); Assert.Null(result.Value.Lifecycle.ResponseCount);
    }
}
