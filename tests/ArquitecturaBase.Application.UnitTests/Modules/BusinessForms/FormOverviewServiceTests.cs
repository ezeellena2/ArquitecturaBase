using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormOverviewServiceTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Either_permission_reads_the_overview_with_only_its_authorized_dimensions(bool read, bool decide)
    {
        using var fixture = new FormOverviewFixture(read, decide);
        var request = new ListFormOverviewRequest { State = "Deactivated", Page = 2 };
        var result = await fixture.Service.ListAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(read, fixture.CanRead);
        Assert.Equal(decide, fixture.CanDecide);
        Assert.Equal(request, fixture.Request);
        Assert.Equal(1, fixture.Reads);
    }

    [Fact]
    public async Task A_record_reader_cannot_use_the_private_pending_filter()
    {
        using var fixture = new FormOverviewFixture(true, false);
        var result = await fixture.Service.ListAsync(new() { PendingOnly = true }, TestContext.Current.CancellationToken);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, fixture.Reads);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("inactive")]
    [InlineData("noPermission")]
    public async Task An_unavailable_actor_never_reads_form_metadata(string reason)
    {
        using var fixture = new FormOverviewFixture();
        if (reason == "anonymous") fixture.Current.UserId = null;
        else if (reason == "inactive") await fixture.Users.SetActiveAsync(fixture.Actor, false, TestContext.Current.CancellationToken);
        else fixture.Permissions.Permissions[fixture.Actor] = [];
        var result = await fixture.Service.ListAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(reason == "anonymous" ? FormDefinitionErrors.Unauthorized : FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, fixture.Reads);
    }

    [Theory]
    [InlineData(BusinessFormsPermissions.RecordsRead)]
    [InlineData(BusinessFormsPermissions.ApprovalsDecide)]
    public async Task Revoking_an_observed_dimension_discards_the_complete_result(string permission)
    {
        using var fixture = new FormOverviewFixture();
        fixture.OnRead = () => fixture.Permissions.Permissions[fixture.Actor] = fixture.Permissions.Permissions[fixture.Actor].Where(value => value != permission).ToArray();
        var result = await fixture.Service.ListAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(1, fixture.Reads);
    }

    [Fact]
    public async Task An_actor_change_during_the_read_discards_the_result()
    {
        using var fixture = new FormOverviewFixture();
        fixture.OnRead = () => fixture.Current.UserId = Guid.NewGuid();
        var result = await fixture.Service.ListAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("page")]
    [InlineData("sort")]
    public async Task Invalid_filters_fail_before_the_reader(string invalid)
    {
        using var fixture = new FormOverviewFixture();
        var request = invalid switch
        {
            "state" => new ListFormOverviewRequest { State = "All" },
            "page" => new ListFormOverviewRequest { Page = 0 },
            _ => new ListFormOverviewRequest { Sort = "responseCount" }
        };
        var result = await fixture.Service.ListAsync(request, TestContext.Current.CancellationToken);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(0, fixture.Reads);
    }
}