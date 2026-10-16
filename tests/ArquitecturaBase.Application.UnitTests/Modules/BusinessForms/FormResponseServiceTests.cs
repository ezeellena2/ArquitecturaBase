using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormResponseServiceTests
{
    [Fact]
    public async Task Navigation_requires_record_read_but_not_template_read()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        var reader = new FakeFormResponseReader();
        var service = Create(reader, user, permissions);
        Assert.True((await service.ListNavigationAsync(new(), CancellationToken.None)).IsFailure);
        Assert.Equal(0, reader.Reads);
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var result = await service.ListNavigationAsync(new(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
    }
    [Fact]
    public async Task Revocation_discards_navigation_items_and_counts()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var reader = new FakeFormResponseReader { BeforeReturn = () => permissions.Permissions.Clear() };
        var result = await Create(reader, user, permissions).ListNavigationAsync(new(), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }
    [Theory]
    [InlineData("list")]
    [InlineData("facets")]
    [InlineData("detail")]
    public async Task Revocation_while_reading_discards_rows_counts_and_detail(string operation)
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var reader = new FakeFormResponseReader { BeforeReturn = () => permissions.Permissions.Clear() };
        reader.ArrangeExport(1);
        var service = Create(reader, user, permissions);
        Result result = operation switch
        {
            "list" => await service.ListAsync(reader.Definition!.Id, new(), CancellationToken.None),
            "facets" => await service.CountFacetsAsync(reader.Definition!.Id, new(), CancellationToken.None),
            _ => await service.GetAsync(reader.ExportRows[0].Record.RecordId, CancellationToken.None),
        };
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }
    [Fact]
    public async Task Invalid_pagination_never_reads_the_database()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService(); permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var reader = new FakeFormResponseReader();
        Assert.True((await Create(reader, user, permissions).ListNavigationAsync(new() { Page = 0 }, CancellationToken.None)).IsFailure);
        Assert.Equal(0, reader.Reads);
    }
    [Fact]
    public async Task List_projects_rows_using_the_definition_schema_for_their_version()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(2);
        reader.ExportRows = reader.ExportRows.Select(row => row with { Record = row.Record with { SchemaJson = "not-read" } }).ToArray();
        var result = await Create(reader, user, permissions).ListAsync(reader.Definition!.Id, new(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "T-000001", "T-000002" }, result.Value.Records.Items.Select(row => row.Number));
    }
    [Fact]
    public async Task Missing_record_returns_the_same_unavailable_error()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService(); permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var result = await Create(new(), user, permissions).GetAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(FormRecordErrors.NotFound, result.Error);
    }
    private static FormResponseService Create(FakeFormResponseReader reader, FakeCurrentUser user, FakePermissionService permissions) =>
        new(reader, new(user, permissions), RequestValidators.For(new FormNavigationRequestValidator(), new ListFormRecordsRequestValidator()), NullLogger<FormResponseService>.Instance, new([]));
}
