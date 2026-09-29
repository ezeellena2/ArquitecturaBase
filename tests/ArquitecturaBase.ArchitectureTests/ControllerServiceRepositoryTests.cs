using System.Reflection;
using ArquitecturaBase.Api.Modules.Control.Controllers;
using ArquitecturaBase.ArchitectureTests.Support;
using NetArchTest.Rules;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Los controllers dependen de servicios y no de repositorios. Un módulo opcional es la misma capa que el núcleo: sus
/// controllers (<c>Api/Modules/&lt;M&gt;/Controllers</c>) siguen las mismas reglas, y los contratos de su módulo cuentan
/// como los del núcleo. Los casos de control, en <c>ControllerServiceRepositoryControls.cs</c>.
/// </summary>
public sealed class ControllerServiceRepositoryTests
{
    private const string DomainNamespace = "ArquitecturaBase.Domain";
    private const string ApplicationNamespace = "ArquitecturaBase.Application";
    private const string ApiNamespace = "ArquitecturaBase.Api";
    private const string ControllersNamespace = ApiNamespace + ".Controllers";
    private const string ServiceInterfacesNamespace = ApplicationNamespace + ".Interfaces.Services";

    // Las carpetas de Interfaces que un controller no toca: persistencia, proveedores y puertos hacia un módulo.
    private static readonly string[] ForbiddenInterfaceFolders = ["Persistence", "Integrations", "Channels"];

    private static readonly Assembly DomainAssembly = Assembly.Load(DomainNamespace);
    private static readonly Assembly ApplicationAssembly = Assembly.Load(ApplicationNamespace);
    private static readonly Assembly ApiAssembly = Assembly.Load(ApiNamespace);
    private static readonly Assembly ControlAssembly = typeof(ControllerServiceRepositoryTests).Assembly;

    [Fact]
    public void Domain_does_not_define_business_persistence_contracts()
    {
        var contracts = DomainAssembly.GetTypes()
            .Where(type => type.IsInterface)
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal)
                || type.Name.EndsWith("Reader", StringComparison.Ordinal))
            .Select(type => type.FullName);

        Assert.Empty(contracts);
    }

    [Fact]
    public void Api_does_not_access_entity_framework_directly()
    {
        var result = Types.InAssembly(ApiAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Controllers_do_not_access_persistence_integrations_or_handlers_directly()
    {
        // Si el namespace se renombrara, la regla de abajo no encontraría ningún tipo y pasaría en silencio.
        Assert.Contains(ApiAssembly.GetTypes(), type => type.ResidesIn(ControllersNamespace));

        AssertSuccessful(ControllerDependencies(ApiAssembly, ApplicationAssembly));
    }

    [Fact]
    public void The_controller_rule_sees_a_module_controller_that_uses_its_module_persistence()
    {
        // Caso de control: NetArchTest compara las dependencias por prefijo, así que las de un módulo se suman a mano.
        var result = ControllerDependencies(ControlAssembly, ControlAssembly);

        Assert.False(result.IsSuccessful);
        Assert.Equal([typeof(ControlRepositoryController).FullName], result.FailingTypeNames);
    }

    [Fact]
    public void Every_controller_injects_an_application_service_interface()
    {
        var controllers = ApiAssembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true }
                && type.ResidesIn(ControllersNamespace)
                && type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(controllers);

        Assert.Empty(controllers.SelectMany(ConstructorProblems));
    }

    [Fact]
    public void The_constructor_rule_accepts_a_module_service_and_rejects_a_module_repository()
    {
        // Casos de control: el servicio de un módulo es una interfaz de servicio; su repositorio, no.
        Assert.Empty(ConstructorProblems(typeof(ControlServiceController)));
        Assert.NotEmpty(ConstructorProblems(typeof(ControlRepositoryController)));
    }

    [Fact]
    public void Old_endpoint_and_handler_pipeline_types_are_absent()
    {
        // Por el namespace canónico: tampoco en un módulo.
        Assert.DoesNotContain(ApiAssembly.GetTypes(), type =>
            CanonicalNamespaceStartsWith(type, ApiNamespace + ".Endpoints")
            || type.Name is "IEndpoint" or "EndpointExtensions");
        Assert.DoesNotContain(ApplicationAssembly.GetTypes(), type =>
            CanonicalNamespaceStartsWith(type, ApplicationNamespace + ".Features")
            || CanonicalNamespaceStartsWith(type, ApplicationNamespace + ".Abstractions")
            || type.Name.StartsWith("ICommandHandler", StringComparison.Ordinal)
            || type.Name.StartsWith("IQueryHandler", StringComparison.Ordinal));
    }

    /// <summary>
    /// Los controllers de <paramref name="controllers"/>, del núcleo o de un módulo, contra lo que no pueden tocar: las
    /// carpetas prohibidas de Interfaces del núcleo y de cada módulo de <paramref name="application"/>.
    /// </summary>
    private static NetArchTest.Rules.TestResult ControllerDependencies(Assembly controllers, Assembly application) =>
        Types.InAssembly(controllers)
            .That()
            .ResideInNamespaceMatching(ModuleNamespaces.Matching(ControllersNamespace))
            .ShouldNot()
            .HaveDependencyOnAny(
            [
                .. ModulesOf(application)
                    .Select(module => $"{ApplicationNamespace}.Modules.{module}")
                    .Prepend(ApplicationNamespace)
                    .SelectMany(root => ForbiddenInterfaceFolders.Select(folder => $"{root}.Interfaces.{folder}")),
                ApplicationNamespace + ".Abstractions.Messaging",
                ApplicationNamespace + ".Features",
                "ArquitecturaBase.Infrastructure",
                "Microsoft.EntityFrameworkCore",
            ])
            .GetResult();

    /// <summary>Los módulos que tienen tipos en <paramref name="application"/>.</summary>
    private static IEnumerable<string> ModulesOf(Assembly application) =>
        application.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(name => name.StartsWith(ApplicationNamespace + ".", StringComparison.Ordinal))
            .Select(ModuleNamespaces.ModuleOf)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// Lo que está mal en el constructor de un controller: tiene uno solo, inyecta al menos una interfaz de
    /// <c>Interfaces.Services</c> y nada más de Application.
    /// </summary>
    private static IEnumerable<string> ConstructorProblems(Type controller)
    {
        var constructors = controller.GetConstructors();

        if (constructors.Length != 1)
        {
            return [$"{controller.Name} has {constructors.Length} public constructors"];
        }

        var parameters = constructors[0].GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var problems = parameters
            .Where(parameter => parameter.Namespace?.StartsWith(ApplicationNamespace, StringComparison.Ordinal) == true
                && !IsInServiceInterfaces(parameter))
            .Select(parameter => $"{controller.Name} injects {parameter.FullName}")
            .ToList();

        if (!parameters.Any(parameter => parameter.IsInterface && IsInServiceInterfaces(parameter)))
        {
            problems.Add($"{controller.Name} does not inject an interface of {ServiceInterfacesNamespace}");
        }

        return problems;
    }

    private static bool IsInServiceInterfaces(Type type) =>
        type.Namespace is { } name && ModuleNamespaces.Canonical(name) == ServiceInterfacesNamespace;

    private static bool CanonicalNamespaceStartsWith(Type type, string prefix) =>
        type.Namespace is { } name && ModuleNamespaces.Canonical(name).StartsWith(prefix, StringComparison.Ordinal);

    private static void AssertSuccessful(NetArchTest.Rules.TestResult result) =>
        Assert.True(result.IsSuccessful, "Types breaking the rule: " + string.Join(", ", result.FailingTypeNames ?? []));
}
