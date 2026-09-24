using System.Reflection;
using NetArchTest.Rules;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class ControllerServiceRepositoryTests
{
    private const string DomainNamespace = "ArquitecturaBase.Domain";
    private const string ApplicationNamespace = "ArquitecturaBase.Application";
    private const string ApiNamespace = "ArquitecturaBase.Api";

    private static readonly Assembly DomainAssembly = Assembly.Load(DomainNamespace);
    private static readonly Assembly ApplicationAssembly = Assembly.Load(ApplicationNamespace);
    private static readonly Assembly ApiAssembly = Assembly.Load(ApiNamespace);

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
        var controllersNamespace = ApiNamespace + ".Controllers";
        if (!ApiAssembly.GetTypes().Any(type => type.Namespace?.StartsWith(controllersNamespace, StringComparison.Ordinal) == true))
        {
            return;
        }

        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace(controllersNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(
                ApplicationNamespace + ".Interfaces.Persistence",
                ApplicationNamespace + ".Interfaces.Integrations",
                ApplicationNamespace + ".Abstractions.Messaging",
                ApplicationNamespace + ".Features",
                "ArquitecturaBase.Infrastructure",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Every_controller_injects_an_application_service_interface()
    {
        var controllers = ApiAssembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true }
                && type.Namespace == ApiNamespace + ".Controllers"
                && type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(controllers);

        foreach (var controller in controllers)
        {
            var constructor = Assert.Single(controller.GetConstructors());
            var parameters = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

            Assert.Contains(parameters, parameter => parameter.IsInterface
                && parameter.Namespace == ApplicationNamespace + ".Interfaces.Services");
            Assert.DoesNotContain(parameters, parameter =>
                parameter.Namespace?.StartsWith(ApplicationNamespace, StringComparison.Ordinal) == true
                && parameter.Namespace != ApplicationNamespace + ".Interfaces.Services");
        }
    }

    [Fact]
    public void Old_endpoint_and_handler_pipeline_types_are_absent()
    {
        Assert.DoesNotContain(ApiAssembly.GetTypes(), type =>
            type.Namespace?.StartsWith(ApiNamespace + ".Endpoints", StringComparison.Ordinal) == true
            || type.Name is "IEndpoint" or "EndpointExtensions");
        Assert.DoesNotContain(ApplicationAssembly.GetTypes(), type =>
            type.Namespace?.StartsWith(ApplicationNamespace + ".Features", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith(ApplicationNamespace + ".Abstractions", StringComparison.Ordinal) == true
            || type.Name.StartsWith("ICommandHandler", StringComparison.Ordinal)
            || type.Name.StartsWith("IQueryHandler", StringComparison.Ordinal));
    }

    private static void AssertSuccessful(NetArchTest.Rules.TestResult result) =>
        Assert.True(result.IsSuccessful, "Types breaking the rule: " + string.Join(", ", result.FailingTypeNames ?? []));
}
