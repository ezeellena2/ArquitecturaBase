using ArquitecturaBase.Application.Abstractions.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// WhatsApp (secciones 9 y 14 del spec). El interruptor es <c>WhatsApp:PhoneNumberId</c>, como el ClientId de Google:
/// sin él queda apagado y la app arranca igual; con él, las opciones se validan al arrancar y sin el token la Api no
/// arranca. Apagado, igual se registran la disponibilidad, un outbox y la salud: Application nunca recibe un null.
/// </summary>
internal static class WhatsAppRegistration
{
    /// <summary>El nombre del HttpClient de Meta. Los tests le cambian el handler por uno que no sale a internet.</summary>
    public const string HttpClientName = "WhatsAppCloud";

    public static IServiceCollection AddWhatsApp(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(WhatsAppOptions.SectionName);
        var enabled = !string.IsNullOrWhiteSpace(section[nameof(WhatsAppOptions.PhoneNumberId)]);

        services.AddSingleton<IWhatsAppAvailability>(new WhatsAppAvailability(enabled));

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

        return services;
    }
}
