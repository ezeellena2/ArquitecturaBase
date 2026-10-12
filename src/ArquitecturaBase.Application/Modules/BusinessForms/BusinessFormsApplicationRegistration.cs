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
        services.Replace(ServiceDescriptor.Scoped<IStructuredFormSource, BusinessFormSource>());
        services.Replace(ServiceDescriptor.Scoped<IStructuredRecordSink, BusinessFormRecordCaptureSink>());
        services.Replace(ServiceDescriptor.Scoped<IStructuredRecordSource, BusinessFormRecordSource>());
        services.TryAddScoped<IBusinessFormRecordCaptureService, Services.BusinessFormRecordRecorder>();
        services.TryAddScoped<IFormDraftService, FormDraftService>();
        services.TryAddScoped<IFormCatalogService, FormCatalogService>();
        services.TryAddScoped<IFormCatalogFiltersService, FormCatalogFiltersService>();
        services.TryAddScoped<FormCatalogAccessGuard>();
        services.TryAddScoped<FormPublicationAccessGuard>();
        services.TryAddScoped<FormPublicationGuard>();
        services.TryAddScoped<FormCatalogPreparationPolicy>();
        services.TryAddScoped<FormDraftAccessGuard>();
        services.TryAddScoped<FormPublicationAcceptanceGuard>();
        services.TryAddScoped<FormPublicationChannelVerifier>();
        services.TryAddScoped<IFormPublicationChannelDispatchService, FormPublicationChannelDispatchService>();
        services.TryAddScoped<IFormPreviewService, FormPreviewService>();
        services.TryAddScoped<IFormPublicationDispatchService, FormPublicationDispatchService>();
        services.TryAddScoped<IFormPublicationStatusService, FormPublicationStatusService>();
        services.TryAddScoped<IFormPublicationAcceptanceService, FormPublicationAcceptanceService>();
        services.TryAddScoped<IBusinessFormPublicationService, BusinessFormPublicationService>();
        services.TryAddScoped<IFormDraftRetentionService, FormDraftRetentionService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, FormPermissionCatalogSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IClientCapabilitySource, FormClientCapabilitySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<SaveFormDraftRequest>, SaveFormDraftRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<ListFormDefinitionsRequest>, ListFormDefinitionsRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<CreateManualCatalogRequest>, CreateManualCatalogRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<UpdateManualCatalogRequest>, UpdateManualCatalogRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<ListFormCatalogsRequest>, ListFormCatalogsRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<ListFormCatalogItemsRequest>, ListFormCatalogItemsRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<FormCatalogFilterOptionsRequest>, FormCatalogFilterOptionsRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<FormCatalogFilterPreviewRequest>, FormCatalogFilterPreviewRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<CreateFiltersCatalogRequest>, CreateFiltersCatalogRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<UpdateFiltersCatalogRequest>, UpdateFiltersCatalogRequestValidator>());
        return services;
    }
}
