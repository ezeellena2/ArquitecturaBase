using ArquitecturaBase.Application.Models.Roles.ReadModels;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Services.Roles;
using ArquitecturaBase.Application.Validation.Roles;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services;

public sealed class RoleServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_roles_maps_reader_data_and_logs_outcome()
    {
        var reader = new FakeRoleReader
        {
            Roles = [new(Guid.NewGuid(), "Lectores", "Solo lectura", false, 2, [Permissions.Users.Read])],
        };
        var logger = new FakeLogger<RoleService>();
        var service = NewService(reader, logger);

        var result = await service.GetRolesAsync(Ct);

        Assert.True(result.IsSuccess);
        var role = Assert.Single(result.Value);
        Assert.Equal("Lectores", role.Name);
        Assert.Equal("Solo lectura", role.Description);
        Assert.False(role.IsSystemRole);
        Assert.Equal(2, role.UserCount);
        Assert.Equal([Permissions.Users.Read], role.Permissions);
        Assert.Equal(Ct, reader.ReceivedCancellation);
        Assert.Equal(1, reader.ListCalls);
        Assert.Equal(
            ["Handling GetRoles", "Handled GetRoles"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Theory]
    [InlineData("es", "Usuarios", "Ver usuarios")]
    [InlineData("en", "Users", "View users")]
    public async Task Get_permissions_keeps_catalog_order_and_request_culture(
        string culture, string usersAreaName, string usersReadName)
    {
        using var cultureScope = new CultureScope(culture);
        var reader = new FakeRoleReader();
        var logger = new FakeLogger<RoleService>();
        var service = NewService(reader, logger);

        var result = await service.GetPermissionsAsync(Ct);

        Assert.True(result.IsSuccess);
        var groups = result.Value.ToArray();
        Assert.Equal(["users", "roles", "settings"], groups.Select(group => group.Area));
        Assert.Equal(usersAreaName, groups[0].Name);
        Assert.Equal(Permissions.All, groups.SelectMany(group => group.Permissions).Select(permission => permission.Code));
        Assert.Equal(usersReadName, groups[0].Permissions.First().Name);
        Assert.Equal(0, reader.ListCalls);
        Assert.Equal(
            ["Handling GetPermissions", "Handled GetPermissions"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    private sealed class FakeRoleReader : IRoleReader
    {
        public IReadOnlyCollection<RoleListItem> Roles { get; set; } = [];

        public int ListCalls { get; private set; }

        public CancellationToken ReceivedCancellation { get; private set; }

        public Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken)
        {
            ListCalls++;
            ReceivedCancellation = cancellationToken;
            return Task.FromResult(Roles);
        }

        public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static RoleService NewService(FakeRoleReader reader, FakeLogger<RoleService> logger) =>
        new(
            reader,
            new UnusedRoleRepository(),
            new UnusedPermissionService(),
            new ServiceRequestValidator<CreateRoleRequest>([new CreateRoleRequestValidator()]),
            new ServiceRequestValidator<UpdateRoleRequest>([new UpdateRoleRequestValidator()]),
            logger);

    private sealed class UnusedRoleRepository : IRoleRepository
    {
        public Task<Guid> CreateAsync(
            string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(
            Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid roleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedPermissionService : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
