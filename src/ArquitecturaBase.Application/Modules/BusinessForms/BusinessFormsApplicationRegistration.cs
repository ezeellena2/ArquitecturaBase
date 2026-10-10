using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Application.Modules.BusinessForms;
public static class BusinessFormsApplicationRegistration
{
    public static IServiceCollection AddBusinessFormsApplication(this IServiceCollection services)
    {
        services.TryAddScoped<IFormDraftService, FormDraftService>();
        services.TryAddScoped<IFormDraftRetentionService, FormDraftRetentionService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, FormPermissionCatalogSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IClientCapabilitySource, FormClientCapabilitySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<SaveFormDraftRequest>, SaveFormDraftRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<ListFormDefinitionsRequest>, ListFormDefinitionsRequestValidator>());
        return services;
    }
}
