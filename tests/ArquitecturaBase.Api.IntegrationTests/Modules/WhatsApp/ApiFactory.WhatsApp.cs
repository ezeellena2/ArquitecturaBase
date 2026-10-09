using ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Delivery;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// La parte del módulo WhatsApp de ApiFactory: WhatsApp prendido con valores inventados, los mensajes en memoria
/// (<see cref="WhatsApp"/>) y el cliente de Meta sin red. Lleva el namespace de la clase del núcleo, no el de su
/// carpeta, para ser otra parte de la misma clase.
/// </summary>
public sealed partial class ApiFactory
{
    /// <summary>El número de WhatsApp de la Api de los tests: los webhooks de otro número se ignoran.</summary>
    public const string WhatsAppPhoneNumberId = "100000000000001";

    /// <summary>El secreto de la app de Meta de los tests, con el que se firman los webhooks. Inventado.</summary>
    public const string WhatsAppAppSecret = "test-app-secret-9f8e7d6c";

    /// <summary>La palabra de verificación del webhook de los tests. Inventada.</summary>
    public const string WhatsAppVerifyToken = "test-verify-token-ab12";

    /// <summary>Los mensajes de WhatsApp que encolaron los casos de uso: nada sale hacia Meta.</summary>
    public CapturingWhatsAppSendQueue WhatsApp { get; } = new();

    internal static void ConfigureWhatsAppSettings(IWebHostBuilder builder)
    {
        // Bajo TestServer no hay IP remota: todos los tests caen en la misma partición del rate limiter.
        builder.UseSetting("RateLimiting:WhatsAppWebhookPermitLimit", "100000");

        // WhatsApp prendido, con valores inventados: los mensajes quedan en memoria (WhatsApp) y el cliente de Meta
        // no tiene salida a internet. WhatsAppRegistrationTests prueba la Api con WhatsApp apagado.
        builder.UseSetting("WhatsApp:PhoneNumberId", WhatsAppPhoneNumberId);
        builder.UseSetting("WhatsApp:AccessToken", "test-access-token");

        // Con los dos secretos del webhook, así que el webhook también está prendido: los tests firman los webhooks
        // con el secreto de la app (MetaWebhook.Sign).
        builder.UseSetting("WhatsApp:AppSecret", WhatsAppAppSecret);
        builder.UseSetting("WhatsApp:VerifyToken", WhatsAppVerifyToken);

        // El bot no contesta solo: los tests llaman a WhatsAppInboundProcessor.ProcessPendingAsync cuando quieren, y así
        // saben qué respondió a qué. WhatsAppBotTests prende el ciclo en segundo plano para probarlo.
        builder.UseSetting("WhatsApp:ProcessInboundInBackground", "false");
        builder.UseSetting("WhatsApp:ProcessOutgoingInBackground", "false");
        builder.UseSetting("RateLimiting:WhatsAppChatReadPermitLimit", "100000");
        builder.UseSetting("RateLimiting:WhatsAppChatWritePermitLimit", "100000");

        // La retención tampoco corre sola: vacía los mensajes viejos de toda la base, y cada Api que arranca un test la
        // correría de nuevo. Los tests llaman a WhatsAppMessageRetentionService.ClearExpiredTextsAsync cuando quieren, y
        // WhatsAppMessageRetentionTests prende el ciclo en segundo plano para probarlo.
        builder.UseSetting("WhatsApp:ApplyMessageRetentionInBackground", "false");

        // El tope diario es global y todos los tests comparten la base y el reloj: con el valor real, sumar tests que
        // mandan códigos por WhatsApp terminaría en 429 intermitentes. WhatsAppLoginCodeTests lo prueba con una Api
        // aparte (WithWebHostBuilder) y un tope chico.
        builder.UseSetting("WhatsApp:DailyAuthCodeLimit", "100000");
    }

    internal void ConfigureWhatsAppServices(IServiceCollection services)
    {
        services.RemoveAll<IWhatsAppSendQueue>();
        services.AddSingleton<IWhatsAppSendQueue>(WhatsApp);
        // A host using the capturing producer must not consume deliveries belonging to a real-queue clone.
        services.AddSingleton<IMessageSource<WhatsAppOutboundMessage>>(provider =>
        {
            using var scope = provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IWhatsAppSendQueue>() is WhatsAppSendQueue
                ? ActivatorUtilities.CreateInstance<WhatsAppMessageSource>(provider)
                : new IdleMessageSource<WhatsAppOutboundMessage>();
        });

        // Por si algo llegara al cliente de Meta sin pasar por la cola: falla acá en lugar de salir a internet. Un test
        // que necesite respuestas de Meta cambia este handler con WithWebHostBuilder.
        services.AddHttpClient(WhatsAppInfrastructureRegistration.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The tests never call the WhatsApp Cloud API.");
    }
}
