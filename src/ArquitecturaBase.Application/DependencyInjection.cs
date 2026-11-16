using System.Reflection;
using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Delivery;
using ArquitecturaBase.Application.Services.Roles;
using ArquitecturaBase.Application.Services.Settings;
using ArquitecturaBase.Application.Services.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<LoginCodeOptions>()
            .BindConfiguration(LoginCodeOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // N16: los plazos de la retención de los datos del ingreso; el worker de Infrastructure lee la misma sección.
        services.AddOptions<IdentityRetentionOptions>()
            .BindConfiguration(IdentityRetentionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // N7: los topes diarios del correo, de la sección Email que Infrastructure lee para mandarlos.
        services.AddOptions<EmailQuotaOptions>()
            .BindConfiguration(EmailQuotaOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                quota => quota.DailyInvitationReserve < quota.DailyLimit,
                "Email:DailyInvitationReserve must be lower than Email:DailyLimit, so codes by email keep some room.")
            .ValidateOnStart();

        // Los puertos hacia un módulo opcional. El canal telefónico, apagado: un módulo que da el canal lo reemplaza, en
        // cualquier orden. Los canales de invitación, uno por canal: el núcleo trae el correo y cada módulo suma el suyo.
        services.TryAddSingleton<IPhoneChannel, DisabledPhoneChannel>();
        services.TryAddSingleton<IAssistantOperations, DisabledAssistantOperations>();
        services.TryAddSingleton<IAssistantProposalLookup, DisabledAssistantProposalLookup>();
        services.TryAddSingleton<IAssistantOperationCompletionSignal, DisabledAssistantOperationCompletionSignal>();
        services.TryAddSingleton<IStructuredFormSource, DisabledStructuredFormSource>();
        services.TryAddSingleton<IStructuredFormChannelStateSource, DisabledStructuredFormChannelStateSource>();
        services.TryAddSingleton<IStructuredFormRetirementSource, DisabledStructuredFormRetirementSource>();
        services.TryAddScoped<IStructuredRecordSink, UnavailableStructuredRecordSink>();
        services.TryAddScoped<IStructuredCatalogProposalSink, UnavailableStructuredCatalogProposalSink>();
        services.TryAddScoped<IStructuredAuthoringProposalChannel, UnavailableStructuredAuthoringProposalChannel>();
        services.TryAddScoped<IStructuredAttachmentReader, UnavailableStructuredAttachmentReader>();
        services.TryAddScoped<IStructuredMissingFormRequestSink, UnavailableStructuredMissingFormRequestSink>();
        services.TryAddScoped<IStructuredDocumentChannel, UnavailableStructuredDocumentChannel>();
        services.TryAddScoped<IStructuredDocumentRowsChannel, UnavailableStructuredDocumentRowsChannel>();
        services.TryAddScoped<IStructuredRecordSource, UnavailableStructuredRecordSource>();
        services.TryAddScoped<IBusinessKnowledgeSource, UnavailableBusinessKnowledgeSource>();
        services.TryAddSingleton<IPermissionCatalog, PermissionCatalog>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAssistantCapabilitySource, UsersSystemQuerySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAssistantCapabilitySource, RolesSystemQuerySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAssistantCapabilitySource, HelpSystemQuerySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IInvitationChannel, EmailInvitationChannel>());
        // N5: the state of an invitation by email comes from the queue.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IInvitationDeliveryStatusSource, EmailInvitationDeliveryStatusSource>());

        // Los helpers y casos de uso se registran explícitamente para mantener visible la composición.
        services.AddScoped<ArquitecturaBase.Application.Interfaces.Services.IPrivateFileCleanupService, ArquitecturaBase.Application.Services.Files.PrivateFileCleanupService>();
        services.AddScoped<UserGuard>();
        services.AddScoped<PermissionDelegationGuard>();
        services.AddScoped<ProfileEmailLinker>();
        services.AddScoped<IDeliveryQueueService, DeliveryQueueService>();
        services.AddScoped<IIdentityRetentionService, IdentityRetentionService>();
        services.AddScoped<IEmailDeliveryService, EmailDeliveryService>();
        services.AddScoped<DestinationCodeIssuer>();
        services.AddScoped<DestinationCodeVerifier>();
        services.AddScoped<PhoneNumberLinker>();
        services.AddScoped<AccountAccessRevoker>();
        services.AddScoped<LoginCodeIssuer>();
        services.AddScoped<EmailQuotaGuard>();
        services.AddScoped<SignInCodeIssuer>();
        services.AddScoped<LoginCodeVerifier>();
        services.AddScoped<LoginAuditRecorder>();
        services.AddScoped<AdministrativeChangeRecorder>();
        services.AddScoped<AccountCreationPolicy>();
        services.AddScoped<UserContactLinker>();
        services.AddScoped<UserInvitationIssuer>();
        services.AddScoped<ILoginMethodsService, LoginMethodsService>();
        services.AddScoped<ILoginCodeService, LoginCodeService>();
        services.AddScoped<IConnectService, ConnectService>();
        services.AddScoped<IExternalLoginService, ExternalLoginService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
        services.AddScoped<IUserQueryService, UserQueryService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IUserAccessService, UserAccessService>();
        services.AddScoped<IProfileQueryService, ProfileQueryService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IProfilePhoneService, ProfilePhoneService>();

        // Los validadores del núcleo: los de un módulo opcional los registra el módulo, con su propio registro.
        services.AddValidatorsFromAssembly(
            typeof(DependencyInjection).Assembly,
            filter: result => !IsOfAModule(result.ValidatorType),
            includeInternalTypes: true);
        services.AddScoped<IRequestValidator, RequestValidator>();

        return services;
    }

    public static IServiceCollection AddApplicationValidatorsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }

    /// <summary>Si el tipo es de un módulo opcional (<c>ArquitecturaBase.Application.Modules.&lt;M&gt;</c>).</summary>
    private static bool IsOfAModule(Type type) =>
        type.Namespace?.StartsWith(typeof(DependencyInjection).Namespace + ".Modules.", StringComparison.Ordinal) == true;
}
