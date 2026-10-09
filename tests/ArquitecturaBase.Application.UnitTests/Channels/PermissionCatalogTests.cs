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

    private sealed class ControlSource : IPermissionCatalogSource
    {
        public IReadOnlyCollection<string> Codes => ["control.read"];
        public string Area(string area) => "Control";
        public string Name(string code) => "Read control";
        public string Description(string code) => "Inspect control data";
    }
}
