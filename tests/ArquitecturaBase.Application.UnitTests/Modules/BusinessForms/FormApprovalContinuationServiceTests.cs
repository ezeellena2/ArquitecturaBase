using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormApprovalContinuationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Next", "es")]
    [InlineData("Previous", "en")]
    public async Task Continuation_forwards_the_authorized_anchor_and_returns_only_the_reader_metadata(string direction, string culture)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var fixture = Arrange(direction);
            var request = new FormApprovalContinuationRequest(fixture.Record.DefinitionId, direction, 1);
            var result = await fixture.Service.GetContinuationAsync(fixture.Task.Id, request, Ct);
            Assert.True(result.IsSuccess);
            Assert.Equal(fixture.Task.Id, result.Value.AnchorTaskId);
            Assert.Equal(request.DefinitionId, result.Value.DefinitionId);
            Assert.Equal(direction, result.Value.Direction);
            Assert.Equal(fixture.Continuation!.Items, result.Value.Items);
            Assert.Equal(8, result.Value.TotalCount);
            Assert.True(result.Value.HasMore);
            Assert.Equal(fixture.User.UserId, fixture.ContinuationActor);
            Assert.Equal(request, fixture.ContinuationRequest);
            Assert.Equal(culture, fixture.ContinuationCulture);
            Assert.Equal(1, fixture.ContinuationReads);
            Assert.Equal(0, fixture.UnitOfWork.Transactions);
            var json = JsonSerializer.SerializeToElement(result.Value, JsonSerializerOptions.Web);
            Assert.Equal(new[] { "anchorTaskId", "definitionId", "direction", "items", "totalCount", "hasMore" }, json.EnumerateObject().Select(property => property.Name));
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Revocation_discards_a_continuation_and_its_absence(bool exists)
    {
        var fixture = Arrange();
        if (!exists) fixture.Continuation = null;
        fixture.BeforeReadReturn = () => fixture.Permissions.Permissions.Clear();
        var result = await fixture.Service.GetContinuationAsync(fixture.Task.Id, new(fixture.Record.DefinitionId, "Next", 20), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(1, fixture.ContinuationReads);
    }

    [Theory]
    [InlineData(null, 20, false)]
    [InlineData("next", 20, false)]
    [InlineData("Next", 0, false)]
    [InlineData("Previous", 101, false)]
    [InlineData("Next", 20, true)]
    public async Task Invalid_continuation_bounds_never_read_approval_metadata(string? direction, int pageSize, bool emptyDefinition)
    {
        var fixture = Arrange();
        var result = await fixture.Service.GetContinuationAsync(fixture.Task.Id,
            new(emptyDefinition ? Guid.Empty : fixture.Record.DefinitionId, direction, pageSize), Ct);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(0, fixture.ContinuationReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_session_or_decide_permission_never_reads_continuation(bool anonymous)
    {
        var fixture = Arrange();
        if (anonymous) fixture.User.UserId = null; else fixture.Permissions.Permissions.Clear();
        var result = await fixture.Service.GetContinuationAsync(fixture.Task.Id, new(fixture.Record.DefinitionId, "Next", 20), Ct);
        Assert.Equal(anonymous ? FormDefinitionErrors.Unauthorized : FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, fixture.ContinuationReads);
    }

    [Fact]
    public async Task A_missing_or_unauthorized_anchor_returns_the_same_not_found()
    {
        var fixture = Arrange(); fixture.Continuation = null;
        var result = await fixture.Service.GetContinuationAsync(fixture.Task.Id, new(fixture.Record.DefinitionId, "Next", 20), Ct);
        Assert.Equal(FormApprovalErrors.NotFound, result.Error);
        Assert.Equal(1, fixture.ContinuationReads);
    }

    private static FormApprovalFixture Arrange(string direction = "Next")
    {
        var fixture = new FormApprovalFixture();
        var task = new FormApprovalTaskResponse(Guid.NewGuid(), fixture.Record.DefinitionId, Guid.NewGuid(), "VIS-000004", "Visits",
            new(fixture.Record.ActorUserId, "Author"), new(fixture.Task.RoleId, "Supervisor"), fixture.Task.RequestedAtUtc,
            1, "Open", true, null, null, null, null);
        fixture.Continuation = new(fixture.Task.Id, fixture.Record.DefinitionId, direction, [new(task, 4)], 8, true);
        return fixture;
    }
}