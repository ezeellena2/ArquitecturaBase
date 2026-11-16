using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Services.Roles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Validation.Roles;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Roles;

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
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
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
        Assert.Equal(["create", "commit"], fixture.Events);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);
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
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
        Assert.Equal(
            ["Handling CreateRole", "CreateRole failed with " + RoleErrors.AlreadyExistsCode],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Update_keeps_system_names_and_admin_permissions()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole(SystemRoles.User, isSystem: true);

        var renamed = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, "Renamed", null, [], 1), Ct);

        Assert.Equal(RoleErrors.SystemRoleCannotChange, renamed.Error);
        Assert.Empty(fixture.Events);

        fixture.Reader.Role = NewRole(SystemRoles.Admin, true, 0, Permissions.Users.Read, Permissions.Roles.Read);
        var changedPermissions = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, SystemRoles.Admin, null, [Permissions.Users.Read], 1), Ct);

        Assert.Equal(RoleErrors.SystemRoleCannotChange, changedPermissions.Error);
        Assert.Empty(fixture.Events);
        Assert.Equal(2, fixture.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Admin_keeps_being_editable_after_a_module_is_removed()
    {
        // B3: Admin guarda el permiso de un módulo que ya no está; el pedido sólo puede traer los del catálogo.
        var fixture = new Fixture();
        string[] catalogPermissions = [.. fixture.Catalog.All.Order(StringComparer.Ordinal)];
        fixture.Reader.Role = NewRole(SystemRoles.Admin, true, 1, [.. catalogPermissions, "removedmodule.manage"]);

        var result = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, SystemRoles.Admin, "Otra descripción", catalogPermissions, 1), Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Update_sorts_permissions_and_commits_without_a_cache_to_invalidate()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole("Reviewers");

        var result = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(
                fixture.Reader.Role.Id,
                "  Editors  ",
                "Can edit",
                [Permissions.Users.Read, Permissions.Roles.Read, Permissions.Users.Read], 1), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.Reader.Role.Id, fixture.Reader.ExcludedRoleId);
        Assert.Equal("Editors", fixture.Reader.LookedUpName);
        Assert.Equal("Editors", fixture.Repository.WrittenName);
        Assert.Equal([Permissions.Roles.Read, Permissions.Users.Read], fixture.Repository.WrittenPermissions);
        // Sin caché de permisos (N9 y N33): el próximo pedido los lee de la base.
        Assert.Equal(["update", "commit"], fixture.Events);
    }

    /// <summary>Una excepción adentro del límite no confirma nada ni deja un Handled o un Failed.</summary>
    [Fact]
    public async Task Failed_update_does_not_commit_or_log_an_outcome()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole("Reviewers");
        fixture.Repository.UpdateFailure = new InvalidOperationException("save failed");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, "Reviewers", null, [], 1), Ct));

        Assert.Equal("save failed", error.Message);
        Assert.Equal(["update"], fixture.Events);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
        Assert.Equal(["Handling UpdateRole"], fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
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
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Delete_commits_without_a_cache_to_invalidate()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole("Reviewers");

        var result = await fixture.Service.DeleteAsync(new DeleteRoleRequest(fixture.Reader.Role.Id), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["delete", "commit"], fixture.Events);
        Assert.Equal(
            ["Handling DeleteRole", "Handled DeleteRole"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Create_rejects_permissions_the_actor_does_not_have()
    {
        var fixture = new Fixture();
        fixture.ActAs(RoleManagersRole());

        var result = await fixture.Service.CreateAsync(
            new CreateRoleRequest("Mesa", null, [Permissions.Roles.Read, Permissions.Users.Manage]), Ct);

        Assert.Equal(RoleErrors.PermissionsNotHeld, result.Error);
        Assert.Empty(fixture.Events);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_rejects_giving_or_taking_away_permissions_the_actor_does_not_have()
    {
        var fixture = new Fixture();
        var own = fixture.ActAs(RoleManagersRole());

        // Darse a sí mismo un permiso que no tiene, en su propio rol.
        fixture.Reader.Role = own;
        var given = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(own.Id, own.Name, null, [.. own.Permissions, Permissions.Users.Manage], 1), Ct);

        // Quitarle a otro rol un permiso que no tiene.
        fixture.Reader.Role = NewRole("Ventas", false, 0, Permissions.Users.Read);
        var taken = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, "Ventas", null, [], 1), Ct);

        Assert.Equal(RoleErrors.PermissionsNotHeld, given.Error);
        Assert.Equal(RoleErrors.PermissionsNotHeld, taken.Error);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Update_lets_the_actor_rename_a_role_with_permissions_they_do_not_have()
    {
        // Sólo cuenta lo que se da o se quita: el nombre y la descripción no dan nada.
        var fixture = new Fixture();
        fixture.ActAs(RoleManagersRole());
        fixture.Reader.Role = NewRole("Ventas", false, 0, Permissions.Users.Read);

        var result = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, "Comercial", "Otra", [Permissions.Users.Read], 1), Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Only_an_administrator_changes_the_permissions_of_user()
    {
        var fixture = new Fixture();
        fixture.ActAs(RoleManagersRole());
        fixture.Reader.Role = NewRole(SystemRoles.User, isSystem: true);

        var byRoleManager = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, SystemRoles.User, null, [Permissions.Roles.Read], 1), Ct);
        var descriptionOnly = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, SystemRoles.User, "Toda cuenta nueva", [], 1), Ct);
        fixture.ActAsAdmin();
        var byAdmin = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, SystemRoles.User, null, [Permissions.Roles.Read], 1), Ct);

        Assert.Equal(RoleErrors.UserRoleAdminOnly, byRoleManager.Error);
        Assert.True(descriptionOnly.IsSuccess);
        Assert.True(byAdmin.IsSuccess);
    }

    [Fact]
    public async Task Update_with_a_stale_revision_conflicts_after_locking_and_without_writing()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole("Ventas", permissions: Permissions.Users.Read) with { Revision = 4 };

        var result = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, "Ventas", "Otra", [Permissions.Users.Read], 3), Ct);

        Assert.Equal(RoleErrors.RevisionConflict, result.Error);
        Assert.Equal([(fixture.Reader.Role.Id, 0)], fixture.Repository.Locked);
        Assert.Null(fixture.Repository.WrittenName);
        Assert.Empty(fixture.Events);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_and_delete_lock_the_role_row_before_reading_it()
    {
        var fixture = new Fixture();
        fixture.Reader.Role = NewRole("Ventas", permissions: Permissions.Users.Read);

        var updated = await fixture.Service.UpdateAsync(
            new UpdateRoleRequest(fixture.Reader.Role.Id, "Ventas", "Otra", [Permissions.Users.Read], 1), Ct);
        var deleted = await fixture.Service.DeleteAsync(new DeleteRoleRequest(fixture.Reader.Role.Id), Ct);

        Assert.True(updated.IsSuccess);
        Assert.True(deleted.IsSuccess);
        Assert.Equal([(fixture.Reader.Role.Id, 0), (fixture.Reader.Role.Id, 1)], fixture.Repository.Locked);
    }

    private static RoleRow RoleManagersRole() =>
        NewRole("Roles", false, 1, Permissions.Roles.Read, Permissions.Roles.Manage);

    private static RoleRow NewRole(
        string name, bool isSystem = false, int userCount = 0, params string[] permissions) =>
        new(Guid.NewGuid(), name, null, isSystem, userCount, permissions, Revision: 1);

    private sealed class Fixture
    {
        public Fixture()
        {
            Reader = new FakeReader(Accounts);
            Repository = new FakeRepository(this);
            UnitOfWork = new FakeUnitOfWork(Events);
            ActAsAdmin();

            // Como RoleRepository: escribir fuera del límite lanza, aunque los eventos y el commit queden en el mismo orden.
            Repository.InTransaction = () => UnitOfWork.InTransaction;
            Service = new RoleService(
                Reader,
                Repository,
                RequestValidators.For(new CreateRoleRequestValidator(new Application.Channels.PermissionCatalog([])),
                    new UpdateRoleRequestValidator(new Application.Channels.PermissionCatalog([]))),
                UnitOfWork,
                Catalog,
                new PermissionDelegationGuard(CurrentUser, Reader, Catalog, new FakeInitialAdmin()),
                Logger);
        }

        public Application.Channels.PermissionCatalog Catalog { get; } = new([]);

        public FakeCurrentUser CurrentUser { get; } = new() { UserId = Guid.CreateVersion7() };

        /// <summary>Los roles de quien pide; no es una cuenta de ningún listado.</summary>
        public InMemoryUserAccounts Accounts { get; } = new();

        /// <summary>Quien pide pasa a tener sólo el rol Admin, con todo el catálogo.</summary>
        public void ActAsAdmin() => Accounts.SetRoles(CurrentUser.UserId!.Value, SystemRoles.Admin);

        /// <summary>Quien pide pasa a tener sólo <paramref name="role"/>, que se suma a los que existen.</summary>
        public RoleRow ActAs(RoleRow role)
        {
            Reader.Others.Add(role);
            Accounts.SetRoles(CurrentUser.UserId!.Value, role.Name);
            return role;
        }

        public FakeUnitOfWork UnitOfWork { get; }

        public FakeReader Reader { get; }

        public FakeRepository Repository { get; }

        public RoleService Service { get; }

        public FakeLogger<RoleService> Logger { get; } = new();

        public List<string> Events { get; } = [];

        public sealed class FakeReader(InMemoryUserAccounts accounts) : IRoleReader
        {
            public async Task<RoleRow?> FindByNameAsync(string name, CancellationToken cancellationToken) =>
                (await ListAllRolesAsync(cancellationToken)).FirstOrDefault(role => role.Name == name);

            /// <summary>Los roles de quien pide, por los nombres que guarda el doble de cuentas.</summary>
            public async Task<IReadOnlyCollection<RoleRow>> ListRolesForUserAsync(Guid userId, CancellationToken cancellationToken)
            {
                var names = await accounts.ListRoleNamesForUserAsync(userId, cancellationToken);

                return [.. (await ListAllRolesAsync(cancellationToken)).Where(role => names.Contains(role.Name, StringComparer.Ordinal))];
            }

            public RoleRow? Role { get; set; }

            /// <summary>Otros roles que existen, además de Admin (con todo el catálogo) y de <see cref="Role"/>.</summary>
            public List<RoleRow> Others { get; } = [];

            public bool NameExists { get; set; }

            public string? LookedUpName { get; private set; }

            public Guid? ExcludedRoleId { get; private set; }

            public int FindByIdCalls { get; private set; }

            public Task<RoleRow?> FindByIdAsync(Guid roleId, CancellationToken cancellationToken)
            {
                FindByIdCalls++;
                return Task.FromResult(Role);
            }

            public Task<bool> ExistsByNameAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken)
            {
                LookedUpName = name;
                ExcludedRoleId = excludedRoleId;
                return Task.FromResult(NameExists);
            }

            public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<IReadOnlyCollection<RoleRow>> ListAllRolesAsync(CancellationToken cancellationToken)
            {
                RoleRow admin = new(
                    Guid.NewGuid(), SystemRoles.Admin, null, true, 1, [.. new Application.Channels.PermissionCatalog([]).All], 1);
                IEnumerable<RoleRow> rows = [admin, .. Others, .. Role is null ? [] : new[] { Role }];

                return Task.FromResult<IReadOnlyCollection<RoleRow>>(
                    [.. rows.DistinctBy(role => role.Name, StringComparer.Ordinal)]);
            }

            public Task<PagedResult<RoleRow>> ListRolesAsync(ListRolesRequest request, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }

        public sealed class FakeRepository(Fixture fixture) : IRoleRepository
        {
            public Guid CreatedId { get; } = Guid.NewGuid();

            public string? WrittenName { get; private set; }

            public string? Description { get; private set; }

            public IReadOnlyCollection<string>? WrittenPermissions { get; private set; }

            public Exception? UpdateFailure { get; set; }

            /// <summary>Si no es null, escribir fuera de la transacción lanza (ver <see cref="TransactionGuard"/>).</summary>
            public Func<bool>? InTransaction { get; set; }

            public Task<Guid> CreateAsync(
                string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
            {
                TransactionGuard.Require(InTransaction);
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
                TransactionGuard.Require(InTransaction);
                WrittenName = name;
                Description = description;
                WrittenPermissions = permissions;
                fixture.Events.Add("update");
                return UpdateFailure is { } error ? Task.FromException(error) : Task.CompletedTask;
            }

            public Task DeleteAsync(Guid roleId, CancellationToken cancellationToken)
            {
                TransactionGuard.Require(InTransaction);
                fixture.Events.Add("delete");
                return Task.CompletedTask;
            }

            /// <summary>Los roles cuya fila se bloqueó, en orden, y cuántas lecturas del lector había hecho antes.</summary>
            public List<(Guid RoleId, int ReadsBefore)> Locked { get; } = [];

            public Task LockAsync(Guid roleId, CancellationToken cancellationToken)
            {
                TransactionGuard.Require(InTransaction);
                Locked.Add((roleId, fixture.Reader.FindByIdCalls));
                return Task.CompletedTask;
            }

            public Task LockForAssignmentAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }
    }
}
