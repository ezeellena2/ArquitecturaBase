namespace ArquitecturaBase.Application.UnitTests.Support;

/// <summary>Los casos de control de la copia de <see cref="ModuleNamespaces.Canonical"/>, con un módulo inventado.</summary>
public sealed class ModuleNamespacesTests
{
    [Fact]
    public void Canonical_drops_the_module_segment()
    {
        Assert.Equal(
            "ArquitecturaBase.Application.Services.Users",
            ModuleNamespaces.Canonical("ArquitecturaBase.Application.Modules.Control.Services.Users"));
    }

    [Fact]
    public void Canonical_keeps_a_core_namespace()
    {
        Assert.Equal(
            "ArquitecturaBase.Application.Services.Users",
            ModuleNamespaces.Canonical("ArquitecturaBase.Application.Services.Users"));
    }

    [Fact]
    public void Canonical_needs_a_dot_after_Modules_and_a_module_name()
    {
        Assert.Equal(
            "ArquitecturaBase.Application.ModulesLegacy.Services",
            ModuleNamespaces.Canonical("ArquitecturaBase.Application.ModulesLegacy.Services"));
        Assert.Equal("ArquitecturaBase.Application.Modules", ModuleNamespaces.Canonical("ArquitecturaBase.Application.Modules"));
    }
}
