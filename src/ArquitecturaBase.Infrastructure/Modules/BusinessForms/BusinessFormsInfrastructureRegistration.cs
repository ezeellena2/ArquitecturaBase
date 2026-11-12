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
        services.TryAddSingleton<IFormWorkbookWriter, Export.ClosedXmlFormWorkbookWriter>();
        services.TryAddSingleton<IFormSpreadsheetReader, Import.ClosedXmlFormSpreadsheetReader>();
        services.TryAddSingleton<IFormDocumentReader, Documents.FormDocumentReader>();
        services.TryAddScoped<IFormPdfDesignReader, FormPdfDesignReader>();
        services.TryAddScoped<IFormPdfDesignRepository, FormPdfDesignRepository>();
        services.TryAddScoped<IFormPdfRenderer, Pdf.SkiaFormPdfRenderer>();
        services.AddOptions<Workers.FormActionWorkerOptions>().BindConfiguration("BusinessForms:ActionWorker");
        services.AddSingleton<Workers.FormActionBackgroundService>();
        services.AddHostedService(provider => provider.GetRequiredService<Workers.FormActionBackgroundService>());
        services.AddOptions<Workers.FormCatalogFeedWorkerOptions>().BindConfiguration("BusinessForms:CatalogFeedWorker")
            .Validate(value => value.PollInterval > TimeSpan.Zero, "BusinessForms:CatalogFeedWorker:PollInterval must be positive.").ValidateOnStart();
        services.AddSingleton<Workers.FormCatalogFeedBackgroundService>();
        services.AddHostedService(provider => provider.GetRequiredService<Workers.FormCatalogFeedBackgroundService>());
        services.TryAddScoped<IFormApprovalReader, FormApprovalReader>();
        services.TryAddScoped<IFormApprovalRepository, FormApprovalRepository>();
        services.TryAddScoped<IFormActionRunRepository, FormActionRunRepository>();
        services.TryAddScoped<IFormActionRunReader, FormActionRunReader>();
        services.TryAddScoped<IFormActionRecipientReader, FormActionRecipientReader>();
        services.TryAddScoped<IFormDocumentRepository, FormDocumentRepository>();
        services.TryAddScoped<IFormRecordResultRepository, FormRecordResultRepository>();
        services.TryAddScoped<IFormRecordResultReader, FormRecordResultReader>();
        services.TryAddScoped<IFormMissingRequestReader, FormMissingRequestReader>();
        services.TryAddScoped<IFormMissingRequestRepository, FormMissingRequestRepository>();
        services.TryAddScoped<IFormSuggestionRepository, FormSuggestionRepository>();
        services.TryAddScoped<IFormSuggestionReader, FormSuggestionReader>();
        services.TryAddScoped<IFormRuleRepository, FormRuleRepository>();
        services.TryAddScoped<IFormRuleReader, FormRuleReader>();
        services.TryAddScoped<IFormPublicationRepository, FormPublicationRepository>();
        services.TryAddScoped<IFormPublicationReader, FormPublicationReader>();
        services.TryAddScoped<IFormSelectionReader, FormSelectionReader>();
        services.TryAddScoped<IFormEntryReader, FormEntryReader>();
        services.TryAddScoped<IFormRecordRepository, FormRecordRepository>();
        services.TryAddScoped<IFormRecordReader, FormRecordReader>();
        services.TryAddScoped<IFormResponseReader, FormResponseReader>();
        services.TryAddScoped<IFormDashboardReader, FormDashboardReader>();
        services.TryAddScoped<IFormOverviewReader, FormOverviewReader>();
        services.TryAddScoped<IFormFileReader, FormFileReader>();
        services.TryAddScoped<IFormCaptureReceiptReader, FormCaptureReceiptReader>();
        services.TryAddSingleton<IFormPublicationProtector, DataProtectionFormPublicationProtector>();
        services.TryAddScoped<IFormDraftRepository, FormDraftRepository>();
        services.TryAddScoped<IFormPersonDataReader, FormPersonDataReader>();
        services.TryAddScoped<IFormPersonDataRepository, FormPersonDataRepository>();
        services.TryAddScoped<IFormCatalogRepository, FormCatalogRepository>();
        services.TryAddScoped<IFormCatalogReader, FormCatalogReader>();
        services.TryAddScoped<IFormCatalogFeedReader, FormCatalogFeedReader>();
        services.TryAddScoped<IFormCatalogItemCodeReader, FormCatalogItemCodeReader>();
        services.TryAddScoped<IFormRecordIssuanceReader, FormRecordIssuanceReader>();
        services.TryAddScoped<IFormCatalogFilterReader, FormCatalogFilterReader>();
        services.TryAddScoped<IFormAuthoringReader, FormAuthoringReader>();
        services.TryAddScoped<IFormLibraryReader, FormLibraryReader>();
        services.TryAddScoped<IFormLibraryRepository, FormLibraryRepository>();
        services.TryAddScoped<IFormFolderRepository, FormFolderRepository>();
        services.TryAddScoped<IFormFolderReader, FormFolderReader>();
        services.TryAddScoped<IFormDefinitionReader, FormDefinitionReader>();
        services.TryAddScoped<IFormLifecycleReader, FormLifecycleReader>();
        services.TryAddScoped<IFormUnusedVersionReader, FormUnusedVersionReader>();
        services.TryAddSingleton<IFormChangeProtector, DataProtectionFormChangeProtector>();
        services.AddHostedService<ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers.FormDraftRetentionBackgroundService>();
        return services;
    }
}
