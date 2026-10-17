using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Application.Modules.WhatsApp;

/// <summary>
/// Lo de Application del módulo WhatsApp (ADR 0007): sus opciones, sus helpers, sus servicios y sus validadores.
/// Program.cs lo llama en el bloque del módulo, después de AddApplication; el núcleo no nombra nada de acá. Se registra
/// entero aunque WhatsApp esté apagado: el núcleo resuelve algunas piezas también apagado (PhoneNumberLinker avisa al
/// participante de WhatsApp, que suelta el contacto del chat, y el canal de invitación responde que WhatsApp no está
/// disponible), y PUT y DELETE /api/me/whatsapp existen siempre. Lo que necesita WhatsApp prendido lo cortan las rutas
/// condicionales.
/// </summary>
public static class WhatsAppApplicationRegistration
{
    public static IServiceCollection AddWhatsAppApplication(this IServiceCollection services)
    {
        services.AddOptions<WhatsAppLoginOptions>()
            .BindConfiguration(WhatsAppLoginOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.HasValidCountries(), WhatsAppLoginOptions.AllowedCountriesError)
            .ValidateOnStart();

        // El canal telefónico del módulo reemplaza al apagado del núcleo (TryAdd allá, Replace acá: vale en cualquier
        // orden). Singleton, como el apagado: solo depende de singletons.
        services.Replace(ServiceDescriptor.Singleton<IPhoneChannel, WhatsAppPhoneChannel>());

        // Compilación local de formularios; disponible sin transporte, sin efectos externos ni aceptación durable.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStructuredFormChannel, WhatsAppStructuredFormChannel>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStructuredRecordOriginSource, WhatsAppStructuredRecordOriginSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStructuredRecordActionChannel, WhatsAppStructuredRecordActionChannel>());
        services.AddScoped<IWhatsAppFormActionService, WhatsAppFormActionService>();
        services.AddScoped<WhatsAppFormActionPolicy>();
        services.AddScoped<WhatsAppManualSendGuard>();
        services.AddScoped<WhatsAppAssistantAccessPolicy>();
        services.AddScoped<WhatsAppAssistantSuggestionPolicy>();
        services.AddScoped<WhatsAppAssistantAcceptancePolicy>();
        services.AddScoped<IWhatsAppAssistantService, WhatsAppAssistantService>();
        services.AddScoped<IWhatsAppAssistantDispatchService, WhatsAppAssistantDispatchService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAssistantCapabilitySource, WhatsAppAssistantCapabilitySource>());

        // La invitación por WhatsApp y su estado de entrega, al lado del correo del núcleo. TryAddEnumerable: uno por canal
        // aunque este registro se llame dos veces.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IInvitationChannel, WhatsAppInvitationChannel>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IInvitationDeliveryStatusSource, WhatsAppInvitationDeliveryStatusSource>());

        // El contacto del chat, atado al número de la cuenta: PhoneNumberLinker toma sus locks y le avisa cada cambio.
        // TryAddEnumerable: registrado dos veces, tomaría dos veces los locks.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPhoneLinkParticipant, WhatsAppPhoneLinkParticipant>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, WhatsAppPermissionCatalogSource>());

        services.AddScoped<IWhatsAppFlowPublicationService, WhatsAppFlowPublicationService>();
        services.AddScoped<IWhatsAppFlowStatusService, WhatsAppFlowStatusService>();
        services.AddScoped<IWhatsAppFlowDispatchService, WhatsAppFlowDispatchService>();
        services.AddScoped<WhatsAppCodeQuotaGuard>();
        services.AddScoped<WhatsAppCodeIssuer>();
        services.AddScoped<WhatsAppContactLinker>();
        services.AddScoped<WhatsAppAccessReplyPolicy>();
        services.AddScoped<IWhatsAppLoginCodeService, WhatsAppLoginCodeService>();
        services.AddScoped<IProfileWhatsAppService, ProfileWhatsAppService>();
        services.AddScoped<IWhatsAppDeliveryService, WhatsAppDeliveryService>();
        services.AddScoped<IWhatsAppWebhookPersistence, WhatsAppWebhookPersistence>();
        services.AddScoped<IWhatsAppWebhookService, WhatsAppWebhookService>();
        services.AddScoped<IWhatsAppInboundService, WhatsAppInboundService>();
        services.AddScoped<WhatsAppAudioAccessPolicy>();
        services.AddScoped<WhatsAppAudioStoragePolicy>();
        services.AddScoped<WhatsAppAudioPreparationPolicy>();
        services.AddScoped<WhatsAppAudioPresentationPolicy>();
        services.AddScoped<IWhatsAppAudioService, WhatsAppAudioService>();
        services.AddScoped<IWhatsAppAudioContentService, WhatsAppAudioContentService>();
        services.AddScoped<IWhatsAppAudioDispatchService, WhatsAppAudioDispatchService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPrivateFileRetentionSource, WhatsAppAudioRetentionSource>());
        services.AddScoped<IWhatsAppChatService, WhatsAppChatService>();
        services.AddScoped<IWhatsAppChatModeService, WhatsAppChatModeService>();
        services.AddScoped<IWhatsAppChatQueryService, WhatsAppChatQueryService>();
        services.AddScoped<IWhatsAppFormService, WhatsAppFormService>();
        services.AddScoped<WhatsAppFormSendPolicy>();
        services.AddScoped<WhatsAppFlowSessionIssuer>();
        services.AddScoped<WhatsAppFlowSessionVerifier>();
        services.AddScoped<WhatsAppFormDispatchPolicy>();
        services.AddScoped<IWhatsAppAssistantContentRetentionService, WhatsAppAssistantContentRetentionService>();
        services.AddScoped<IWhatsAppFormSendService, WhatsAppFormSendService>();
        services.AddScoped<WhatsAppFlowMediaRecorder>();
        services.AddScoped<IWhatsAppFlowReplyService, WhatsAppFlowReplyService>();
        services.AddScoped<IWhatsAppFlowReplyStateService, WhatsAppFlowReplyStateService>();
        services.AddScoped<IWhatsAppFormMessageDispatchService, WhatsAppFormMessageDispatchService>();
        services.AddScoped<IWhatsAppChatDispatchService, WhatsAppChatDispatchService>();

        // Solo los validadores del módulo: los del núcleo los registra AddApplication, que deja afuera estos.
        services.AddValidatorsFromAssembly(
            typeof(WhatsAppApplicationRegistration).Assembly,
            filter: result => IsOfTheModule(result.ValidatorType),
            includeInternalTypes: true);

        return services;
    }

    private static bool IsOfTheModule(Type type)
    {
        var moduleNamespace = typeof(WhatsAppApplicationRegistration).Namespace!;

        return type.Namespace is { } name
            && (name == moduleNamespace || name.StartsWith(moduleNamespace + ".", StringComparison.Ordinal));
    }
}
