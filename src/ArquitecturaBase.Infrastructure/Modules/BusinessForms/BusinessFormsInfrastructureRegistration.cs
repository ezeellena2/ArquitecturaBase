using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms;
public static class BusinessFormsInfrastructureRegistration
{
    public static IServiceCollection AddBusinessFormsInfrastructure(this IServiceCollection services)
    {
        services.TryAddScoped<IFormDraftRepository, FormDraftRepository>();
        services.TryAddScoped<IFormDefinitionReader, FormDefinitionReader>();
        services.TryAddSingleton<IFormChangeProtector, DataProtectionFormChangeProtector>();
        services.AddHostedService<ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers.FormDraftRetentionBackgroundService>();
        return services;
    }
}
