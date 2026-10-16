using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
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
    [Fact]
    public async Task Invalid_pagination_never_reads_the_database()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService(); permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var reader = new FakeFormResponseReader();
        Assert.True((await Create(reader, user, permissions).ListNavigationAsync(new() { Page = 0 }, CancellationToken.None)).IsFailure);
        Assert.Equal(0, reader.Reads);
    }
    private static FormResponseService Create(FakeFormResponseReader reader, FakeCurrentUser user, FakePermissionService permissions) =>
        new(reader, new(user, permissions), RequestValidators.For(new FormNavigationRequestValidator(), new ListFormRecordsRequestValidator()), NullLogger<FormResponseService>.Instance);
}
