using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.Arca.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Application.Modules.Arca;
public static class ArcaApplicationRegistration
{
    public static IServiceCollection AddArcaApplication(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, ArcaPermissionCatalogSource>());
        return services;
    }
}
