using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormDefinitionLifecycleReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task A_page_joins_lifecycle_once_and_preserves_paging_and_per_definition_actions()
    {
        var fixture = new FormDefinitionLifecycleReadFixture();
        var result = await fixture.Policy.ListAsync(new(), fixture.User.UserId!.Value, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal((3, 2, 7), (result.Value.Page, result.Value.PageSize, result.Value.TotalCount));
        Assert.Equal(1, fixture.FactReads);
        Assert.Equal([fixture.Id, fixture.OtherId], fixture.RequestedIds);
        var active = result.Value.Items[0]; var inactive = result.Value.Items[1];
        Assert.True(active.CanEdit); Assert.True(active.Lifecycle.CanDeactivate); Assert.False(active.Lifecycle.CanReactivate);
        Assert.Equal(10, active.Lifecycle.ResponseCount); Assert.Equal(1, active.Lifecycle.PendingApprovalCount);
        Assert.Equal("VIS-000011", active.Lifecycle.NextRecordNumber);
        Assert.False(inactive.Lifecycle.CanDeactivate); Assert.True(inactive.Lifecycle.CanReactivate);
        Assert.Equal(20, inactive.Lifecycle.ResponseCount); Assert.Equal("FAC-000021", inactive.Lifecycle.NextRecordNumber);
        Assert.NotNull(inactive.Lifecycle.DeactivatedBy);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_and_page_hide_all_record_counts_when_records_permission_is_revoked_during_the_read(bool detail)
    {
        var fixture = new FormDefinitionLifecycleReadFixture();
        fixture.BeforeFactsReturn = () => fixture.Permissions.Permissions[fixture.User.UserId!.Value] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish];
        FormDefinitionLifecycleResponse[] values;
        if (detail)
        {
            var result = await fixture.Policy.FindAsync(fixture.Id, fixture.User.UserId!.Value, Ct);
            Assert.True(result.IsSuccess); values = [result.Value.Lifecycle];
        }
        else
        {
            var result = await fixture.Policy.ListAsync(new(), fixture.User.UserId!.Value, Ct);
            Assert.True(result.IsSuccess); values = result.Value.Items.Select(row => row.Lifecycle).ToArray();
        }
        Assert.All(values, lifecycle => { Assert.Null(lifecycle.ResponseCount); Assert.Null(lifecycle.PendingApprovalCount);
            Assert.Null(lifecycle.NextRecordNumber); Assert.True(lifecycle.NextVersionNumber > 0); });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_and_page_discard_everything_when_read_permission_is_revoked_during_the_read(bool detail)
    {
        var fixture = new FormDefinitionLifecycleReadFixture();
        fixture.BeforeFactsReturn = () => fixture.Permissions.Permissions[fixture.User.UserId!.Value] = [];
        var error = detail ? (await fixture.Policy.FindAsync(fixture.Id, fixture.User.UserId!.Value, Ct)).Error
            : (await fixture.Policy.ListAsync(new(), fixture.User.UserId!.Value, Ct)).Error;
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, error.Code);
    }
    [Fact]
    public async Task Lifecycle_is_a_detail_sibling_and_does_not_enter_the_editable_definition()
    {
        var fixture = new FormDefinitionLifecycleReadFixture();
        fixture.Permissions.Permissions[fixture.User.UserId!.Value] = [BusinessFormsPermissions.Read];
        var result = await fixture.Policy.FindAsync(fixture.Id, fixture.User.UserId!.Value, Ct);
        Assert.True(result.IsSuccess);
        var json = JsonSerializer.SerializeToElement(result.Value, JsonSerializerOptions.Web);
        Assert.True(json.TryGetProperty("lifecycle", out _));
        Assert.False(json.GetProperty("definition").TryGetProperty("lifecycle", out _));
        Assert.False(result.Value.Lifecycle.CanDeactivate); Assert.False(result.Value.Lifecycle.CanReactivate);
        Assert.False(result.Value.Capabilities.CanSave); Assert.False(result.Value.Capabilities.CanPublish);
        Assert.Null(fixture.RecoveryActor);
    }
}
