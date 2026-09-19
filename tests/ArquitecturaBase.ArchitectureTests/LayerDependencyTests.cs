using System.Reflection;
using System.Runtime.InteropServices;
using NetArchTest.Rules;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private const string DomainNamespace = "ArquitecturaBase.Domain";
    private const string ApplicationNamespace = "ArquitecturaBase.Application";
    private const string InfrastructureNamespace = "ArquitecturaBase.Infrastructure";
    private const string ApiNamespace = "ArquitecturaBase.Api";

    private static readonly Assembly DomainAssembly = Assembly.Load(DomainNamespace);
    private static readonly Assembly ApplicationAssembly = Assembly.Load(ApplicationNamespace);
    private static readonly Assembly InfrastructureAssembly = Assembly.Load(InfrastructureNamespace);
    private static readonly Assembly ApiAssembly = Assembly.Load(ApiNamespace);

    [Fact]
    public void Domain_does_not_depend_on_other_layers_or_frameworks()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ApplicationNamespace,
                InfrastructureNamespace,
                ApiNamespace,
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions",
                "FluentValidation",
                "Npgsql")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Domain_only_references_the_base_class_library()
    {
        // Solo cuenta como BCL lo que realmente vive en la carpeta del shared framework
        // (Microsoft.NETCore.App); un paquete NuGet como System.CommandLine no está ahí
        // aunque su nombre empiece con "System".
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();

        var references = DomainAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !File.Exists(Path.Combine(runtimeDirectory, name + ".dll")));

        Assert.Empty(references);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_api_or_persistence()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                InfrastructureNamespace,
                ApiNamespace,
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Npgsql")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Api_uses_infrastructure_only_from_the_composition_root()
    {
        // Program.cs (namespace global) es el único que llama a AddInfrastructure y a las migraciones.
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace(ApiNamespace)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        AssertSuccessful(result);
    }

    private static void AssertSuccessful(NetArchTest.Rules.TestResult result) =>
        Assert.True(result.IsSuccessful, "Types breaking the rule: " + string.Join(", ", result.FailingTypeNames ?? []));
}
