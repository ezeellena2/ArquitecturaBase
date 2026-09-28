using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Services.Roles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.Validation.Roles;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Roles;

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

    [Fact]
    public async Task A_missing_role_is_not_found()
    {
        var reader = new FakeRoleReader
        {
            Roles = [new(Guid.NewGuid(), "Lectores", "Solo lectura", false, 2, [Permissions.Users.Read])],
        };
        var logger = new FakeLogger<RoleService>();
        var service = NewService(reader, logger);
        var missingId = Guid.NewGuid();

        var result = await service.GetRoleAsync(missingId, Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(RoleErrors.NotFound, result.Error);
        Assert.Equal(missingId, reader.FoundId);
        Assert.Equal(
            ["Handling GetRole", "GetRole failed with Roles.Role.NotFound"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task The_detail_is_the_row_with_its_permissions()
    {
        var roleId = Guid.NewGuid();
        var reader = new FakeRoleReader
        {
            Roles =
            [
                new(Guid.NewGuid(), "Otro", null, false, 0, []),
                new(roleId, "Admin", "Todo", true, 3, [Permissions.Roles.Manage, Permissions.Users.Read]),
            ],
        };
        var logger = new FakeLogger<RoleService>();
        var service = NewService(reader, logger);

        var result = await service.GetRoleAsync(roleId, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(roleId, result.Value.Id);
        Assert.Equal("Admin", result.Value.Name);
        Assert.Equal("Todo", result.Value.Description);
        Assert.True(result.Value.IsSystemRole);
        Assert.Equal(3, result.Value.UserCount);
        Assert.Equal([Permissions.Roles.Manage, Permissions.Users.Read], result.Value.Permissions);
        Assert.Equal(Ct, reader.ReceivedCancellation);
        Assert.Equal(0, reader.ListCalls);
        Assert.Equal(
            ["Handling GetRole", "Handled GetRole"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task A_sort_outside_the_list_is_a_validation_error_and_does_not_reach_the_reader()
    {
        var reader = new FakeRoleReader();
        var logger = new FakeLogger<RoleService>();
        var service = NewService(reader, logger);

        var result = await service.ListRolesAsync(new ListRolesRequest { Sort = "normalizedName" }, Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.True(error.Errors.ContainsKey("sort"));
        Assert.Null(reader.PageRequest);
        Assert.Equal(
            ["Handling ListRoles", "ListRoles failed with Validation.Failed"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.Equal(LogLevel.Warning, logger.Collector.GetSnapshot()[1].Level);
    }

    [Fact]
    public async Task The_page_is_translated_to_responses_keeping_its_totals()
    {
        var roleId = Guid.NewGuid();
        var reader = new FakeRoleReader
        {
            Page = new PagedResult<RoleRow>(
                [new(roleId, "Lectores", "Solo lectura", false, 2, [Permissions.Users.Read])],
                Page: 2,
                PageSize: 1,
                TotalCount: 3),
        };
        var logger = new FakeLogger<RoleService>();
        var service = NewService(reader, logger);
        var request = new ListRolesRequest { Page = 2, PageSize = 1, Sort = "-createdAtUtc", Search = "lect" };

        var result = await service.ListRolesAsync(request, Ct);

        Assert.True(result.IsSuccess);
        Assert.Same(request, reader.PageRequest);
        Assert.Equal(Ct, reader.ReceivedCancellation);
        Assert.Equal(0, reader.ListCalls);
        var role = Assert.Single(result.Value.Items);
        Assert.Equal(roleId, role.Id);
        Assert.Equal("Lectores", role.Name);
        Assert.Equal("Solo lectura", role.Description);
        Assert.False(role.IsSystemRole);
        Assert.Equal(2, role.UserCount);
        Assert.Equal([Permissions.Users.Read], role.Permissions);
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(1, result.Value.PageSize);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
        Assert.Equal(
            ["Handling ListRoles", "Handled ListRoles"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    private sealed class FakeRoleReader : IRoleReader
    {
        public IReadOnlyCollection<RoleRow> Roles { get; set; } = [];

        public int ListCalls { get; private set; }

        public CancellationToken ReceivedCancellation { get; private set; }

        public PagedResult<RoleRow> Page { get; set; } = new([], 1, PagedRequest.DefaultPageSize, 0);

        public ListRolesRequest? PageRequest { get; private set; }

        public Task<IReadOnlyCollection<RoleRow>> ListAllRolesAsync(CancellationToken cancellationToken)
        {
            ListCalls++;
            ReceivedCancellation = cancellationToken;
            return Task.FromResult(Roles);
        }

        public Task<PagedResult<RoleRow>> ListRolesAsync(ListRolesRequest request, CancellationToken cancellationToken)
        {
            PageRequest = request;
            ReceivedCancellation = cancellationToken;
            return Task.FromResult(Page);
        }

        public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Guid? FoundId { get; private set; }

        public Task<RoleRow?> FindByIdAsync(Guid roleId, CancellationToken cancellationToken)
        {
            FoundId = roleId;
            ReceivedCancellation = cancellationToken;
            return Task.FromResult(Roles.FirstOrDefault(role => role.Id == roleId));
        }

        public Task<bool> ExistsByNameAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static RoleService NewService(FakeRoleReader reader, FakeLogger<RoleService> logger) =>
        new(
            reader,
            new UnusedRoleRepository(),
            new UnusedPermissionService(),
            RequestValidators.For(
                new CreateRoleRequestValidator(), new UpdateRoleRequestValidator(), new ListRolesRequestValidator()),
            new FakeUnitOfWork(),
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
