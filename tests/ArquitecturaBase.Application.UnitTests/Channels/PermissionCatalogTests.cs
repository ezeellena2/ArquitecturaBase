using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Application.UnitTests.Channels;

public sealed class PermissionCatalogTests
{
    [Fact]
    public void Without_extensions_only_base_permissions_are_available()
    {
        var catalog = new PermissionCatalog([]);
        Assert.Equal(Permissions.All, catalog.All);
        Assert.False(catalog.Contains("control.read"));
    }

    [Fact]
    public void Registered_extensions_are_available_to_validation_and_presentation()
    {
        var catalog = new PermissionCatalog([new ControlSource()]);
        Assert.True(catalog.Contains("control.read"));
        Assert.Equal("Control", catalog.Area("control"));
        Assert.Equal("Read control", catalog.Name("control.read"));
        Assert.Equal("Inspect control data", catalog.Description("control.read"));
        Assert.Equal(Permissions.All.Append("control.read"), catalog.All);
    }

    [Fact]
    public void Duplicate_codes_fail_composition_instead_of_shadowing_a_permission()
    {
        Assert.Throws<InvalidOperationException>(() => new PermissionCatalog([new ControlSource(), new ControlSource()]));
    }

    [Fact]
    public void The_core_catalog_says_what_managing_users_and_roles_includes()
    {
        var catalog = new PermissionCatalog([]);

        Assert.Equal(
            [(Permissions.Users.Manage, Permissions.Users.Read), (Permissions.Roles.Manage, Permissions.Roles.Read)],
            catalog.All.SelectMany(code => catalog.Includes(code).Select(included => (code, included))));
    }

    [Fact]
    public void A_permission_includes_what_its_inclusions_include_in_catalog_order()
    {
        var catalog = new PermissionCatalog([new ControlSource(
            ["control.read", "control.edit", "control.manage"],
            new() { ["control.manage"] = ["control.edit"], ["control.edit"] = ["control.read"] })]);

        Assert.Equal(["control.read", "control.edit"], catalog.Includes("control.manage"));
        Assert.Equal(["control.read"], catalog.Includes("control.edit"));
        Assert.Empty(catalog.Includes("control.read"));
        Assert.Empty(catalog.Includes("unknown.code"));
    }

    [Fact]
    public void What_some_codes_allow_adds_what_each_one_includes_and_drops_codes_outside_the_catalog()
    {
        var catalog = new PermissionCatalog([]);

        var allowed = catalog.Effective([Permissions.Users.Manage, Permissions.Roles.Read, "removedmodule.manage"]);

        Assert.Equal(
            [Permissions.Roles.Read, Permissions.Users.Manage, Permissions.Users.Read],
            allowed.Order(StringComparer.Ordinal));
        Assert.Empty(catalog.Effective([]));
    }

    [Fact]
    public void An_inclusion_outside_the_catalog_fails_composition()
    {
        Assert.Throws<InvalidOperationException>(() => new PermissionCatalog(
            [new ControlSource(["control.manage"], new() { ["control.manage"] = ["control.read"] })]));
    }

    [Fact]
    public void An_inclusion_of_another_area_fails_composition()
    {
        Assert.Throws<InvalidOperationException>(() => new PermissionCatalog(
            [new ControlSource(["control.manage"], new() { ["control.manage"] = [Permissions.Users.Read] })]));
    }

    [Fact]
    public void A_source_declares_inclusions_only_for_its_own_codes()
    {
        Assert.Throws<InvalidOperationException>(() => new PermissionCatalog(
            [new ControlSource(["control.read"], new() { [Permissions.Users.Manage] = [Permissions.Users.Read] })]));
    }

    private sealed class ControlSource(
        IReadOnlyCollection<string>? codes = null,
        Dictionary<string, IReadOnlyCollection<string>>? includes = null) : IPermissionCatalogSource
    {
        public IReadOnlyCollection<string> Codes => codes ?? ["control.read"];
        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Includes =>
            includes ?? new Dictionary<string, IReadOnlyCollection<string>>();
        public string Area(string area) => "Control";
        public string Name(string code) => "Read control";
        public string Description(string code) => "Inspect control data";
    }
}
