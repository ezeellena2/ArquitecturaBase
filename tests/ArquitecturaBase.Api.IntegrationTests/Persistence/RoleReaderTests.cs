using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

[Collection(ApiTestGroup.Name)]
public sealed class RoleReaderTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deleted_users_do_not_count_but_role_details_and_permissions_remain()
    {
        var name = UniqueName();
        var roleId = await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
            .CreateAsync(name, "Solo lectura", [Permissions.Users.Read], Ct));

        var account = await factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            var created = await users.CreateAsync(
                Email.Create(TestEmails.Unique("rolecount")).Value,
                phone: null,
                phoneConfirmed: false,
                displayName: null,
                culture: "es",
                Ct);
            await users.SetRolesAsync(created.Id, [name], Ct);

            return created;
        });

        var before = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IRoleReader>()
            .FindByIdAsync(roleId, Ct));
        Assert.NotNull(before);
        Assert.Equal(1, before.UserCount);

        await factory.InTransactionAsync(services =>
            services.GetRequiredService<IUserRepository>().DeleteAsync(account.Id, Ct));

        var after = await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IRoleReader>();
            var detail = await reader.FindByIdAsync(roleId, Ct);
            var listed = await reader.ListAllRolesAsync(Ct);

            return (detail, listed);
        });
        Assert.NotNull(after.detail);
        Assert.Equal(name, after.detail.Name);
        Assert.Equal("Solo lectura", after.detail.Description);
        Assert.False(after.detail.IsSystemRole);
        Assert.Equal(0, after.detail.UserCount);
        Assert.Equal([Permissions.Users.Read], after.detail.Permissions);
        var listedRole = Assert.Single(after.listed, role => role.Id == roleId);
        Assert.Equal(after.detail.UserCount, listedRole.UserCount);
        Assert.Equal(after.detail.Permissions, listedRole.Permissions);
    }

    [Fact]
    public async Task Name_lookup_uses_identity_normalization_and_can_exclude_the_same_role()
    {
        var name = UniqueName();
        var roleId = await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
            .CreateAsync(name, description: null, [], Ct));

        var result = await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IRoleReader>();
            var names = await reader.ListRoleNamesAsync(Ct);
            var matching = await reader.ExistsByNameAsync(name.ToUpperInvariant(), excludedRoleId: null, Ct);
            var ownExcluded = await reader.ExistsByNameAsync(name.ToUpperInvariant(), roleId, Ct);
            var otherExcluded = await reader.ExistsByNameAsync(name.ToUpperInvariant(), Guid.NewGuid(), Ct);

            return (names, matching, ownExcluded, otherExcluded);
        });

        Assert.Contains(name, result.names);
        Assert.True(result.matching);
        Assert.False(result.ownExcluded);
        Assert.True(result.otherExcluded);
    }

    // La base es compartida: cada test del listado crea sus roles con un prefijo propio y busca por él.

    [Fact]
    public async Task A_page_brings_its_roles_by_name_and_the_totals_of_the_search()
    {
        var prefix = UniqueName();
        var ids = await CreateRolesAsync(
            (prefix + "-b", "Segundo"), (prefix + "-a", "Primero"), (prefix + "-c", null));

        var first = await ListAsync(new ListRolesRequest { Search = prefix, PageSize = 2 });
        var second = await ListAsync(new ListRolesRequest { Search = prefix, PageSize = 2, Page = 2 });

        // Sin orden pedido, por nombre ascendente.
        Assert.Equal([prefix + "-a", prefix + "-b"], first.Items.Select(role => role.Name));
        Assert.Equal([prefix + "-c"], second.Items.Select(role => role.Name));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.True(first.HasNext);
        Assert.False(second.HasNext);
        var row = first.Items[0];
        Assert.Equal(ids[1], row.Id);
        Assert.Equal("Primero", row.Description);
        Assert.False(row.IsSystemRole);
        Assert.Equal(0, row.UserCount);
        Assert.Equal([Permissions.Users.Read], row.Permissions);
    }

    [Fact]
    public async Task A_descending_name_sort_reverses_the_page()
    {
        var prefix = UniqueName();
        await CreateRolesAsync((prefix + "-a", null), (prefix + "-b", null), (prefix + "-c", null));

        var page = await ListAsync(new ListRolesRequest { Search = prefix, Sort = "-name" });

        Assert.Equal([prefix + "-c", prefix + "-b", prefix + "-a"], page.Items.Select(role => role.Name));
    }

    [Fact]
    public async Task Sorting_by_creation_follows_the_clock_and_not_the_name()
    {
        var prefix = UniqueName();

        // El reloj falso no avanza solo: sin Advance, las tres altas tendrían la misma fecha.
        var ids = new List<Guid>();
        foreach (var suffix in new[] { "-c", "-a", "-b" })
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(1));
            ids.AddRange(await CreateRolesAsync((prefix + suffix, null)));
        }

        var ascending = await ListAsync(new ListRolesRequest { Search = prefix, Sort = "createdAtUtc" });
        var descending = await ListAsync(new ListRolesRequest { Search = prefix, Sort = "-createdAtUtc" });

        Assert.Equal(ids, ascending.Items.Select(role => role.Id));
        Assert.Equal(Enumerable.Reverse(ids), descending.Items.Select(role => role.Id));
    }

    [Fact]
    public async Task Roles_created_at_the_same_instant_keep_a_stable_order_by_id_across_pages()
    {
        var prefix = UniqueName();

        // Las dos altas, sin mover el reloj: la misma fecha. El desempate por Id decide y no cambia entre páginas.
        var ids = await CreateRolesAsync((prefix + "-b", null), (prefix + "-a", null));

        var first = await ListAsync(new ListRolesRequest { Search = prefix, Sort = "createdAtUtc", PageSize = 1 });
        var second = await ListAsync(new ListRolesRequest { Search = prefix, Sort = "createdAtUtc", PageSize = 1, Page = 2 });

        Assert.Equal(2, first.TotalCount);
        Guid[] actualIds = [Assert.Single(first.Items).Id, Assert.Single(second.Items).Id];
        Assert.Equal(ids.Order(), actualIds);
    }

    [Fact]
    public async Task Percent_and_underscore_in_the_search_are_literal()
    {
        var prefix = UniqueName();
        await CreateRolesAsync(
            (prefix + "-100%", null), (prefix + "-1000", null), (prefix + "-a_b", null), (prefix + "-axb", null));

        var percent = await ListAsync(new ListRolesRequest { Search = prefix + "-100%" });
        var underscore = await ListAsync(new ListRolesRequest { Search = prefix + "-a_b" });

        Assert.Equal([prefix + "-100%"], percent.Items.Select(role => role.Name));
        Assert.Equal([prefix + "-a_b"], underscore.Items.Select(role => role.Name));
    }

    [Fact]
    public async Task The_search_also_looks_in_the_description_and_a_role_without_one_is_still_found_by_name()
    {
        var prefix = UniqueName();
        var token = "desc" + Guid.NewGuid().ToString("N")[..8];
        await CreateRolesAsync((prefix + "-sin", null), (prefix + "-con", "Incluye " + token + " en el medio"));

        // Sin distinguir mayúsculas y con espacios alrededor, que se recortan.
        var byName = await ListAsync(new ListRolesRequest { Search = "  " + prefix.ToUpperInvariant() + "  " });
        var byDescription = await ListAsync(new ListRolesRequest { Search = token.ToUpperInvariant() });

        Assert.Equal([prefix + "-con", prefix + "-sin"], byName.Items.Select(role => role.Name));
        Assert.Equal([prefix + "-con"], byDescription.Items.Select(role => role.Name));
    }

    private async Task<Guid[]> CreateRolesAsync(params (string Name, string? Description)[] roles)
    {
        var ids = new List<Guid>();
        foreach (var (name, description) in roles)
        {
            // Cada alta en su transacción, para que el reloj pueda avanzar entre una y otra. Los que tienen descripción
            // llevan un permiso, para ver que la página los trae.
            string[] permissions = description is null ? [] : [Permissions.Users.Read];
            ids.Add(await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
                .CreateAsync(name, description, permissions, Ct)));
        }

        return [.. ids];
    }

    private Task<PagedResult<RoleRow>> ListAsync(ListRolesRequest request) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IRoleReader>().ListRolesAsync(request, Ct));

    private static string UniqueName() => "rol-" + Guid.NewGuid().ToString("N")[..8];
}
