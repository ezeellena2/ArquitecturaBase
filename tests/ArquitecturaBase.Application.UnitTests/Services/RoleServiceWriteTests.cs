using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Roles.ReadModels;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Services.Roles;
using ArquitecturaBase.Application.Validation.Roles;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services;

public sealed class RoleServiceWriteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_validates_all_fields_before_reading_or_writing()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateAsync(
            new CreateRoleRequest(null, new string('x', ValidationRules.RoleDescriptionMaxLength + 1), ["unknown.permission"]), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(["description", "name", "permissions"], error.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(fixture.Events);
        Assert.Equal(
            ["Handling CreateRole", "CreateRole failed with Validation.Failed"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Create_trims_the_name_and_removes_duplicate_permissions()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateAsync(
            new CreateRoleRequest("  Reviewers  ", "Description", [Permissions.Users.Read, Permissions.Users.Read, Permissions.Roles.Read]), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.Repository.CreatedId, result.Value);
        Assert.Equal("Reviewers", fixture.Reader.LookedUpName);
        Assert.Null(fixture.Reader.ExcludedRoleId);
        Assert.Equal("Reviewers", fixture.Repository.WrittenName);
        Assert.Equal("Description", fixture.Repository.Description);
        Assert.Equal([Permissions.Users.Read, Permissions.Roles.Read], fixture.Repository.WrittenPermissions);
        Assert.Equal(["create"], fixture.Events);
    }

    [Fact]
    public async Task Create_rejects_an_existing_normalized_name_before_writing()
    {
        var fixture = new Fixture();
        fixture.Reader.NameExists = true;

        var result = await fixture.Service.CreateAsync(new CreateRoleRequest(" Existing ", null, []), Ct);

        Assert.Equal(RoleErrors.AlreadyExists, result.Error);
        Assert.Equal("Existing", fixture.Reader.LookedUpName);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Update_keeps_system_names_and_admin_permissions()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole(SystemRoles.User, isSystem: true);

        var renamed = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, "Renamed", null, []), Ct);

        Assert.Equal(RoleErrors.SystemRoleCannotChange, renamed.Error);
        Assert.Empty(fixture.Events);

        fixture.Reader.Role = NewRole(SystemRoles.Admin, true, 0, Permissions.Users.Read, Permissions.Roles.Read);
        var changedPermissions = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, SystemRoles.Admin, null, [Permissions.Users.Read]), Ct);

        Assert.Equal(RoleErrors.SystemRoleCannotChange, changedPermissions.Error);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Update_sort_permissions_and_invalidates_cache_after_the_repository_returns()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole("Reviewers");

        var result = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(
                fixture.Reader.Role.Id,
                "  Editors  ",
                "Can edit",
                [Permissions.Users.Read, Permissions.Roles.Read, Permissions.Users.Read]), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.Reader.Role.Id, fixture.Reader.ExcludedRoleId);
        Assert.Equal("Editors", fixture.Reader.LookedUpName);
        Assert.Equal("Editors", fixture.Repository.WrittenName);
        Assert.Equal([Permissions.Roles.Read, Permissions.Users.Read], fixture.Repository.WrittenPermissions);
        Assert.Equal(["update", "invalidate"], fixture.Events);
    }

    [Fact]
    public async Task Failed_update_does_not_invalidate_the_cache()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole("Reviewers");
        fixture.Repository.UpdateFailure = new InvalidOperationException("save failed");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, "Reviewers", null, []), Ct));

        Assert.Equal("save failed", error.Message);
        Assert.Equal(["update"], fixture.Events);
    }

    [Fact]
    public async Task Delete_rejects_assigned_users_with_the_existing_count()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole("Reviewers", userCount: 2);

        var result = await fixture.Service.DeleteAsync(new DeleteRoleRequest(fixture.Reader.Role.Id), Ct);

        Assert.Equal(RoleErrors.HasUsersCode, result.Error.Code);
        Assert.Equal(2, result.Error.Metadata![RoleErrors.UserCountKey]);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Delete_invalidates_the_cache_after_repository_deletion()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole("Reviewers");

        var result = await fixture.Service.DeleteAsync(new DeleteRoleRequest(fixture.Reader.Role.Id), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["delete", "invalidate"], fixture.Events);
    }

    private static RoleListItem NewRole(
        string name, bool isSystem = false, int userCount = 0, params string[] permissions) =>
        new(Guid.NewGuid(), name, null, isSystem, userCount, permissions);

    private sealed class Fixture
    {
        public Fixture()
        {
            Reader = new FakeReader();
            Repository = new FakeRepository(this);
            Service = new RoleService(
                Reader,
                Repository,
                new FakePermissionService(this),
                new ServiceRequestValidator<CreateRoleRequest>([new CreateRoleRequestValidator()]),
                new ServiceRequestValidator<UpdateRoleRequest>([new UpdateRoleRequestValidator()]),
                Logger);
        }

        public FakeReader Reader { get; }

        public FakeRepository Repository { get; }

        public RoleService Service { get; }

        public FakeLogger<RoleService> Logger { get; } = new();

        public List<string> Events { get; } = [];

        public sealed class FakeReader : IRoleReader
        {
            public RoleListItem? Role { get; set; }

            public bool NameExists { get; set; }

            public string? LookedUpName { get; private set; }

            public Guid? ExcludedRoleId { get; private set; }

            public Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
                Task.FromResult(Role);

            public Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken)
            {
                LookedUpName = name;
                ExcludedRoleId = excludedRoleId;
                return Task.FromResult(NameExists);
            }

            public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }

        public sealed class FakeRepository(Fixture fixture) : IRoleRepository
        {
            public Guid CreatedId { get; } = Guid.NewGuid();

            public string? WrittenName { get; private set; }

            public string? Description { get; private set; }

            public IReadOnlyCollection<string>? WrittenPermissions { get; private set; }

            public Exception? UpdateFailure { get; set; }

            public Task<Guid> CreateAsync(
                string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
            {
                WrittenName = name;
                Description = description;
                WrittenPermissions = permissions;
                fixture.Events.Add("create");
                return Task.FromResult(CreatedId);
            }

            public Task UpdateAsync(
                Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions,
                CancellationToken cancellationToken)
            {
                WrittenName = name;
                Description = description;
                WrittenPermissions = permissions;
                fixture.Events.Add("update");
                return UpdateFailure is { } error ? Task.FromException(error) : Task.CompletedTask;
            }

            public Task DeleteAsync(Guid roleId, CancellationToken cancellationToken)
            {
                fixture.Events.Add("delete");
                return Task.CompletedTask;
            }
        }

        private sealed class FakePermissionService(Fixture fixture) : IPermissionService
        {
            public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken)
            {
                fixture.Events.Add("invalidate");
                return Task.CompletedTask;
            }
        }
    }
}
