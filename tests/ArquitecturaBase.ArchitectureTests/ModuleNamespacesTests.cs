using System.Text.RegularExpressions;
using ArquitecturaBase.Api.Modules.Control.Controllers;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Los nombres de un módulo opcional, sobre los que se apoyan las reglas de arquitectura para ver también al módulo. Las
/// cadenas son inventadas (los módulos Control y Sms no existen): estos tests son del núcleo y siguen corriendo sin
/// ningún módulo.
/// </summary>
public sealed class ModuleNamespacesTests
{
    [Theory]
    [InlineData("ArquitecturaBase.Application.Modules.Control.Services.X", "ArquitecturaBase.Application.Services.X")]
    [InlineData("ArquitecturaBase.Api.Modules.Sms.Controllers", "ArquitecturaBase.Api.Controllers")]
    [InlineData("ArquitecturaBase.Domain.Modules.Control", "ArquitecturaBase.Domain")]
    [InlineData(
        "ArquitecturaBase.Infrastructure.Modules.Control.Persistence.Readers.ControlReader",
        "ArquitecturaBase.Infrastructure.Persistence.Readers.ControlReader")]
    public void Canonical_drops_the_module_segment(string name, string expected)
    {
        Assert.Equal(expected, ModuleNamespaces.Canonical(name));
    }

    [Theory]
    [InlineData("ArquitecturaBase.Application.Services.Users")]
    [InlineData("ArquitecturaBase.Application.ModulesLegacy.Services")]
    [InlineData("ArquitecturaBase.Application.Modules")]
    [InlineData("ArquitecturaBase.Application.Modules.control.Services")]
    [InlineData("ArquitecturaBase.ArchitectureTests.Modules.Control")]
    [InlineData("Other.Application.Modules.Control.Services")]
    public void What_is_not_a_module_keeps_its_name_and_has_no_module(string name)
    {
        Assert.Equal(name, ModuleNamespaces.Canonical(name));
        Assert.Null(ModuleNamespaces.ModuleOf(name));
    }

    [Theory]
    [InlineData("ArquitecturaBase.Application.Modules.Control.Services.X", "Control")]
    [InlineData("ArquitecturaBase.Domain.Modules.Sms", "Sms")]
    [InlineData("ArquitecturaBase.Api.Modules.Sms2.Controllers.SmsController", "Sms2")]
    public void ModuleOf_names_the_module(string name, string module)
    {
        Assert.Equal(module, ModuleNamespaces.ModuleOf(name));
    }

    [Theory]
    [InlineData("ArquitecturaBase.Api.Controllers", true)]
    [InlineData("ArquitecturaBase.Api.Controllers.Users", true)]
    [InlineData("ArquitecturaBase.Api.Modules.Control.Controllers", true)]
    [InlineData("ArquitecturaBase.Api.Modules.Control.Controllers.Users", true)]
    [InlineData("ArquitecturaBase.Api.ControllersLegacy", false)]
    [InlineData("ArquitecturaBase.Api.Modules.Control.ControllersLegacy", false)]
    [InlineData("ArquitecturaBase.Api.Modules.Control.Contracts", false)]
    [InlineData("ArquitecturaBase.Api", false)]
    [InlineData("ArquitecturaBase.Api.Modules.Control", false)]
    [InlineData("ArquitecturaBase.Application.Controllers", false)]
    public void Matching_accepts_the_namespace_and_the_same_inside_a_module(string @namespace, bool matches)
    {
        var pattern = ModuleNamespaces.Matching("ArquitecturaBase.Api.Controllers");

        Assert.Equal(matches, Regex.IsMatch(@namespace, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ResidesIn_compares_the_canonical_namespace()
    {
        // Un controller de un módulo inventado (ControllerServiceRepositoryControls.cs) vive con los del núcleo.
        Assert.True(typeof(ControlServiceController).ResidesIn("ArquitecturaBase.Api.Controllers"));
        Assert.True(typeof(ControlServiceController).ResidesIn("ArquitecturaBase.Api"));
        Assert.False(typeof(ControlServiceController).ResidesIn("ArquitecturaBase.Api.Contracts"));
    }

    [Fact]
    public void Matching_needs_a_canonical_namespace()
    {
        Assert.Throws<ArgumentException>(() => ModuleNamespaces.Matching("ArquitecturaBase.Api.Modules.Control.Controllers"));
        Assert.Throws<ArgumentException>(() => ModuleNamespaces.Matching("Other.Api.Controllers"));
    }
}
