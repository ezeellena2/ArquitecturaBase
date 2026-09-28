using System.Reflection;
using System.Runtime.CompilerServices;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class ApplicationServicesTests
{
    private const string ServicesNamespace = "ArquitecturaBase.Application.Services";
    private const string ServiceInterfacesNamespace = "ArquitecturaBase.Application.Interfaces.Services";

    private static readonly Assembly ApplicationAssembly = Assembly.Load("ArquitecturaBase.Application");

    [Fact]
    public void Every_application_service_implements_a_service_interface()
    {
        // Los controllers inyectan la interfaz, nunca la clase: un *Service sin su contrato en Interfaces.Services no se
        // puede usar desde la Api sin romper ControllerServiceRepositoryTests. Las piezas internas de un área que no
        // terminan en Service (UserGuard, LoginCodeIssuer, PhoneNumberLinker) no entran en esta regla.
        var services = ApplicationAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.ResidesIn(ServicesNamespace)
                && type.Name.EndsWith("Service", StringComparison.Ordinal)
                && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .ToArray();

        Assert.NotEmpty(services);

        var withoutContract = services
            .Where(service => !service.GetInterfaces().Any(contract => contract.ResidesIn(ServiceInterfacesNamespace)))
            .Select(service => service.FullName);

        Assert.Empty(withoutContract);
    }
}
