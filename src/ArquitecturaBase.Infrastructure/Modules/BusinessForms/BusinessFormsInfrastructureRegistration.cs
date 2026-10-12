using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Seed;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms;
public static class BusinessFormsInfrastructureRegistration
{
    public static IServiceCollection AddBusinessFormsInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers.FormPublicationWorkerOptions>().BindConfiguration("BusinessForms:PublicationWorker");
        services.AddSingleton<ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers.FormPublicationBackgroundService>();
        services.AddHostedService(provider => provider.GetRequiredService<ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers.FormPublicationBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDatabaseSeedParticipant, FormVersionPresentationSeeder>());
        services.TryAddScoped<IFormPublicationRepository, FormPublicationRepository>();
        services.TryAddScoped<IFormPublicationReader, FormPublicationReader>();
        services.TryAddScoped<IFormSelectionReader, FormSelectionReader>();
        services.TryAddScoped<IFormRecordRepository, FormRecordRepository>();
        services.TryAddScoped<IFormRecordReader, FormRecordReader>();
        services.TryAddScoped<IFormCaptureReceiptReader, FormCaptureReceiptReader>();
        services.TryAddSingleton<IFormPublicationProtector, DataProtectionFormPublicationProtector>();
        services.TryAddScoped<IFormDraftRepository, FormDraftRepository>();
        services.TryAddScoped<IFormCatalogRepository, FormCatalogRepository>();
        services.TryAddScoped<IFormCatalogReader, FormCatalogReader>();
        services.TryAddScoped<IFormDefinitionReader, FormDefinitionReader>();
        services.TryAddSingleton<IFormChangeProtector, DataProtectionFormChangeProtector>();
        services.AddHostedService<ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers.FormDraftRetentionBackgroundService>();
        return services;
    }
}
