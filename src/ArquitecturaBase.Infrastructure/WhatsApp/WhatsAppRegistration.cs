using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// WhatsApp (secciones 7, 9 y 14 del spec). El interruptor es <c>WhatsApp:PhoneNumberId</c>, como el ClientId de
/// Google: sin él queda apagado y la app arranca igual; con él, las opciones se validan al arrancar y sin el token la
/// Api no arranca. Apagado, igual se registran la disponibilidad, un outbox y la salud: Application nunca recibe un
/// null. El webhook se prende aparte, con sus dos secretos.
/// </summary>
internal static class WhatsAppRegistration
{
    /// <summary>El nombre del HttpClient de Meta. Los tests le cambian el handler por uno que no sale a internet.</summary>
    public const string HttpClientName = "WhatsAppCloud";

    /// <summary>Sin un valor: dice qué clave falta, para qué sirve y cuál es la de desarrollo.</summary>
    public const string MissingPublicOriginMessage =
        "Missing Authentication:Issuer, the public origin of the web app (https://localhost:5173/ in development): the "
        + "WhatsApp webhook is on, and the bot answers with sign-in links to that address. Set it, or turn the webhook "
        + "off by removing WhatsApp:AppSecret and WhatsApp:VerifyToken.";

    public static IServiceCollection AddWhatsApp(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(WhatsAppOptions.SectionName);
        var enabled = !string.IsNullOrWhiteSpace(section[nameof(WhatsAppOptions.PhoneNumberId)]);

        // El webhook necesita además sus dos secretos. Con uno solo, el validador de las opciones frena el arranque.
        var webhookEnabled = enabled
            && !string.IsNullOrWhiteSpace(section[nameof(WhatsAppOptions.AppSecret)])
            && !string.IsNullOrWhiteSpace(section[nameof(WhatsAppOptions.VerifyToken)]);

        services.AddSingleton<IWhatsAppAvailability>(new WhatsAppAvailability(enabled, webhookEnabled));

        // Readiness y no liveness (sin el tag "live"): un reinicio automático no arregla un token vencido o sin
        // permisos. Hace falta arreglarlo en Meta o cargar otro, y recién ahí reiniciar.
        services.AddSingleton<WhatsAppHealth>();
        services.AddHealthChecks().AddCheck<WhatsAppHealthCheck>(WhatsAppHealthCheck.Name);

        if (!enabled)
        {
            services.AddSingleton<IWhatsAppOutbox, DisabledWhatsAppOutbox>();

            return services;
        }

        services.AddOptions<WhatsAppOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WhatsAppOptions>, WhatsAppOptionsValidator>();

        services.AddSingleton<WhatsAppOutbox>();
        services.AddSingleton<IWhatsAppOutbox>(serviceProvider => serviceProvider.GetRequiredService<WhatsAppOutbox>());
        services.AddHostedService<WhatsAppSenderBackgroundService>();

        services.AddHttpClient<IWhatsAppCloudClient, WhatsAppCloudClient>(
                HttpClientName,
                client => client.BaseAddress = WhatsAppCloudClient.GraphApiAddress)

            // Sin los logs por defecto del HttpClient: en Trace, el de los encabezados tapa el token en el texto
            // ("Authorization: *") pero lo deja entero en el estado estructurado, que un exportador de logs manda
            // tal cual. El resultado de cada envío lo registra la cola, con el número enmascarado.
            .RemoveAllLoggers()

            // ServiceDefaults le pone a todos los HttpClient la resiliencia estándar, que reintenta los 5xx y los
            // timeouts. Un POST a Meta reintentado puede llegar dos veces: acá se reemplaza por la misma resiliencia
            // sin reintentos para los métodos que no son seguros, con sus timeouts y su circuit breaker. Los
            // reintentos los hace la cola.
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(resilience => resilience.Retry.DisableForUnsafeHttpMethods());

        AddWebhook(services, webhookEnabled);

        return services;
    }

    /// <summary>
    /// El webhook (sección 7 del spec). Sin sus dos secretos, la Api no mapea sus rutas y avisa al arrancar qué falta;
    /// sin ninguno de los dos es lo esperado hasta que se configura el túnel, así que no es un error. Con el webhook
    /// llegan los mensajes, y con ellos el procesador que los contesta.
    /// </summary>
    private static void AddWebhook(IServiceCollection services, bool webhookEnabled)
    {
        if (!webhookEnabled)
        {
            services.AddHostedService<WhatsAppWebhookOffNotice>();

            return;
        }

        services.AddSingleton<IWhatsAppSignatureValidator, WhatsAppSignatureValidator>();
        services.AddSingleton<IWhatsAppWebhookReader, WhatsAppWebhookReader>();

        // El bot manda enlaces a la web, y la dirección de la web es el origen público. Sin él, el primer mensaje
        // fallaría recién al contestarlo: mejor que la Api no arranque y diga qué falta.
        services.AddOptions<WhatsAppOptions>()
            .Validate<IPublicOrigin>((_, publicOrigin) => publicOrigin.Value is not null, MissingPublicOriginMessage);

        services.AddSingleton<WhatsAppInboundSignal>();
        services.AddSingleton<IWhatsAppInboundSignal>(serviceProvider => serviceProvider.GetRequiredService<WhatsAppInboundSignal>());

        // Uno solo, con su propio tipo: los tests lo llaman con ProcessPendingAsync y el host lo corre en segundo plano.
        services.AddSingleton<WhatsAppInboundProcessor>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<WhatsAppInboundProcessor>());
    }
}
