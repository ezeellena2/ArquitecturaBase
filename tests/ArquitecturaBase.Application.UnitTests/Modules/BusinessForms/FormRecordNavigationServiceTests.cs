using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormRecordNavigationServiceTests
{
    [Theory]
    [InlineData(BusinessFormsPermissions.RecordsRead, true)]
    [InlineData(BusinessFormsPermissions.Read, false)]
    [InlineData(BusinessFormsPermissions.ApprovalsDecide, false)]
    public async Task Only_record_read_authorizes_navigation(string permission, bool allowed)
    {
        var (reader, user, permissions, service) = Create();
        permissions.Permissions[user.UserId!.Value] = [permission];
        var result = await service.FindRecordNavigationAsync(reader.Definition!.Id, reader.RecordNavigation!.RecordId, new(), CancellationToken.None);
        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed)
        {
            Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
            Assert.Equal(0, reader.Reads);
        }
    }

    [Fact]
    public async Task An_anonymous_request_never_reads_navigation()
    {
        var (reader, user, _, service) = Create();
        user.UserId = null;
        var result = await service.FindRecordNavigationAsync(reader.Definition!.Id, Guid.NewGuid(), new(), CancellationToken.None);
        Assert.Equal(FormDefinitionErrors.Unauthorized, result.Error);
        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public async Task Invalid_pagination_is_rejected_before_reading_the_definition()
    {
        var (reader, _, _, service) = Create();
        var result = await service.FindRecordNavigationAsync(reader.Definition!.Id, Guid.NewGuid(), new() { Page = 0 }, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public async Task A_filter_outside_the_definition_snapshot_never_reads_navigation()
    {
        var (reader, _, _, service) = Create();
        var result = await service.FindRecordNavigationAsync(reader.Definition!.Id, Guid.NewGuid(), new() { VersionIds = [Guid.NewGuid()] }, CancellationToken.None);
        Assert.Equal(FormRecordErrors.FilterInvalid, result.Error);
        Assert.Equal(1, reader.DefinitionReads);
        Assert.Equal(0, reader.NavigationReads);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Navigation_forwards_the_filter_and_returns_only_identifiers_and_global_position(string culture)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var (reader, _, _, service) = Create();
            var request = new ListFormRecordsRequest { Page = 5, PageSize = 1, Sort = "-number", Search = "private search",
                State = "Received", ActorIds = [Guid.NewGuid()], VersionIds = [reader.Definition!.ActiveVersionId] };
            var expected = reader.RecordNavigation!;
            var result = await service.FindRecordNavigationAsync(reader.Definition.Id, expected.RecordId, request, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(new FormRecordNavigationResponse(expected.RecordId, expected.PreviousId, expected.NextId, expected.Position, expected.TotalCount), result.Value);
            Assert.Equal(request with { DisplayCulture = culture }, reader.NavigationRequest);
            Assert.Equal(1, reader.NavigationReads);
            Assert.Empty(reader.ExportBatchSizes);
            Assert.Equal(0, reader.ChainReads);
            var json = JsonSerializer.SerializeToElement(result.Value, JsonSerializerOptions.Web);
            Assert.Equal(new[] { "recordId", "previousId", "nextId", "position", "totalCount" }, json.EnumerateObject().Select(property => property.Name));
            Assert.DoesNotContain("private", json.GetRawText(), StringComparison.Ordinal);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_missing_definition_or_filtered_record_returns_the_same_not_found(bool definitionExists)
    {
        var (reader, _, _, service) = Create();
        var definitionId = reader.Definition!.Id;
        if (!definitionExists) reader.Definition = null;
        reader.RecordNavigation = null;
        var result = await service.FindRecordNavigationAsync(definitionId, Guid.NewGuid(), new(), CancellationToken.None);
        Assert.Equal(FormRecordErrors.NotFound, result.Error);
        Assert.Equal(definitionExists ? 1 : 0, reader.NavigationReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Revocation_discards_navigation_and_even_its_absence(bool recordExists)
    {
        var (reader, _, permissions, service) = Create();
        var recordId = reader.RecordNavigation!.RecordId;
        if (!recordExists) reader.RecordNavigation = null;
        reader.BeforeReturn = () => permissions.Permissions.Clear();
        var result = await service.FindRecordNavigationAsync(reader.Definition!.Id, recordId, new(), CancellationToken.None);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(1, reader.NavigationReads);
    }

    private static (FakeFormResponseReader Reader, FakeCurrentUser User, FakePermissionService Permissions, FormResponseService Service) Create()
    {
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(0);
        reader.RecordNavigation = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 13, 25);
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var service = new FormResponseService(reader, new(user, permissions), RequestValidators.For(new ListFormRecordsRequestValidator()),
            NullLogger<FormResponseService>.Instance, new([]), new FormSuggestionFixture());
        return (reader, user, permissions, service);
    }
}
