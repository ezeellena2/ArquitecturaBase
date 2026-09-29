using ArquitecturaBase.Api.Modules.WhatsApp.Routing;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.Modules.WhatsApp;

/// <summary>
/// Lo de Api del módulo WhatsApp (ADR 0007): las rutas condicionales y el límite del webhook. Program.cs lo llama en el
/// bloque del módulo, después de AddPresentation; el núcleo no nombra nada de acá. Las opciones y las convenciones de
/// MVC se componen con Configure, así que el orden de registro no cambia el resultado.
/// </summary>
public static class WhatsAppApiRegistration
{
    /// <summary>La política de rate limit del webhook, por IP.</summary>
    public const string WebhookRateLimitPolicy = "whatsapp-webhook";

    public static IServiceCollection AddWhatsAppApi(this IServiceCollection services)
    {
        // MVC saca las acciones de WhatsApp que están apagadas mientras arma sus descriptores, antes de mapear las rutas.
        services.AddOptions<MvcOptions>()
            .Configure<IWhatsAppAvailability>((options, availability) =>
                options.Conventions.Add(new ConditionalWhatsAppRouteConvention(availability)));

        services.AddOptions<WhatsAppWebhookRateLimitOptions>()
            .BindConfiguration(WhatsAppWebhookRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Generosa: la llama Meta, que agrupa las novedades y reintenta lo que no recibió su 200. Frena a quien mande
        // firmas inventadas a mansalva, que igual se rechazan, y cada pedido cuesta leer hasta 5 MB.
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(WebhookRateLimitPolicy, context =>
        {
            var settings = context.RequestServices.GetRequiredService<IOptions<WhatsAppWebhookRateLimitOptions>>().Value;

            return RateLimitingExtensions.FixedWindowByIp(
                context, settings.WhatsAppWebhookPermitLimit, settings.WhatsAppWebhookWindowMinutes);
        }));

        return services;
    }
}
