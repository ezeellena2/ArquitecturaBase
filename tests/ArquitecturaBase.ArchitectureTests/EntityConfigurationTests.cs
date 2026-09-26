using System.Reflection;
using ArquitecturaBase.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class EntityConfigurationTests
{
    private const string ConfigurationsNamespace = "ArquitecturaBase.Infrastructure.Persistence.Configurations";

    private static readonly Assembly DomainAssembly = Assembly.Load("ArquitecturaBase.Domain");
    private static readonly Assembly InfrastructureAssembly = Assembly.Load("ArquitecturaBase.Infrastructure");

    [Fact]
    public void Every_domain_entity_has_its_entity_type_configuration()
    {
        // Cada entidad se mapea con su IEntityTypeConfiguration<T> en Persistence/Configurations: ahí se deciden la
        // tabla, los largos, los índices y los filtros, en lugar de dejárselo a las convenciones de EF.
        var entities = DomainAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(Entity)))
            .ToArray();

        Assert.NotEmpty(entities);

        var configured = InfrastructureAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.ResidesIn(ConfigurationsNamespace))
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
            .Select(contract => contract.GetGenericArguments()[0])
            .ToHashSet();

        var withoutConfiguration = entities
            .Where(entity => !configured.Contains(entity))
            .Select(entity => entity.FullName);

        Assert.Empty(withoutConfiguration);
    }
}
