using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

/// <summary>
/// El módulo WhatsApp registra lo suyo con su propio registro (<see cref="WhatsAppApplicationRegistration"/>), que
/// Program.cs llama en el bloque del módulo: el registro del núcleo no nombra nada del módulo.
/// </summary>
public sealed class WhatsAppApplicationRegistrationTests
{
    private static readonly string ModuleNamespace = typeof(WhatsAppApplicationRegistration).Namespace!;

    [Fact]
    public void The_module_registers_its_services_only_through_its_own_registration()
    {
        var core = new ServiceCollection();
        core.AddApplication();

        // Sin el registro del módulo no queda nada del módulo: ni un contrato ni una implementación.
        Assert.DoesNotContain(core, descriptor => IsOfTheModule(descriptor.ServiceType)
            || (descriptor.ImplementationType is { } implementation && IsOfTheModule(implementation)));

        var both = new ServiceCollection();
        both.AddApplication().AddWhatsAppApplication();

        // Con los dos, cada contrato de servicio del módulo queda registrado una vez, y cada validador del módulo.
        var contracts = typeof(WhatsAppApplicationRegistration).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == ModuleNamespace + ".Interfaces.Services")
            .ToArray();
        var validators = typeof(WhatsAppApplicationRegistration).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && IsOfTheModule(type)
                && type.GetInterfaces().Any(contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IValidator<>)))
            .ToArray();

        Assert.NotEmpty(contracts);
        Assert.NotEmpty(validators);
        Assert.All(contracts, contract => Assert.Single(both, descriptor => descriptor.ServiceType == contract));
        Assert.All(validators, validator => Assert.Contains(both, descriptor => descriptor.ImplementationType == validator));
    }

    [Fact]
    public void The_module_replaces_the_disabled_phone_channel_in_either_order()
    {
        // El núcleo lo agrega con TryAdd y el módulo lo reemplaza con Replace: queda uno solo, el del módulo, sea cual sea
        // el orden de los registros.
        var coreFirst = new ServiceCollection();
        coreFirst.AddApplication().AddWhatsAppApplication();
        var moduleFirst = new ServiceCollection();
        moduleFirst.AddWhatsAppApplication().AddApplication();

        ServiceCollection[] bothOrders = [coreFirst, moduleFirst];

        Assert.All(bothOrders, services =>
        {
            var channel = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPhoneChannel));
            Assert.Equal(typeof(WhatsAppPhoneChannel), channel.ImplementationType);
            Assert.Equal(ServiceLifetime.Singleton, channel.Lifetime);
        });
    }

    [Fact]
    public void Without_the_module_the_phone_channel_is_the_disabled_one()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        var channel = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPhoneChannel));
        Assert.Equal(typeof(DisabledPhoneChannel), channel.ImplementationType);
    }

    private static bool IsOfTheModule(Type type) =>
        type.Namespace is { } name && (name == ModuleNamespace || name.StartsWith(ModuleNamespace + ".", StringComparison.Ordinal));
}
