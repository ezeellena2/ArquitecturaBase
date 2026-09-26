using System.Xml.Linq;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class ApplicationPackagesTests
{
    // Application no conoce EF Core ni ASP.NET Core: solo usa las abstracciones de Microsoft.Extensions (logging y
    // opciones) y FluentValidation. La lista es explícita a propósito, en lugar de aceptar cualquier Microsoft.Extensions.*:
    // sumar un paquete a Application es una decisión de arquitectura, no algo que se cuela porque el prefijo coincide.
    private static readonly string[] AllowedPackages =
    [
        "FluentValidation",
        "FluentValidation.DependencyInjectionExtensions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Options.ConfigurationExtensions",
        "Microsoft.Extensions.Options.DataAnnotations",
    ];

    [Fact]
    public void Application_only_references_allowed_packages()
    {
        var forbidden = ReadApplicationProject()
            .Descendants("PackageReference")
            .Select(reference => (reference.Attribute("Include") ?? reference.Attribute("Update"))?.Value ?? string.Empty)
            .Except(AllowedPackages, StringComparer.OrdinalIgnoreCase);

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Application_does_not_reference_shared_frameworks()
    {
        // Un FrameworkReference a Microsoft.AspNetCore.App traería ASP.NET Core entero sin pasar por la lista de paquetes.
        Assert.Empty(ReadApplicationProject().Descendants("FrameworkReference"));
    }

    private static XDocument ReadApplicationProject() =>
        XDocument.Load(Path.Combine(
            SolutionRoot.FullPath, "src", "ArquitecturaBase.Application", "ArquitecturaBase.Application.csproj"));
}
