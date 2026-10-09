using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// Una persona que le escribe al bot desde su número, con webhooks firmados como los de Meta, y una vuelta del
/// procesador para que el bot le conteste. Lo usan los tests que necesitan un chat de verdad: un contacto guardado, un
/// enlace mandado a ese chat o el botón de la invitación.
/// </summary>
internal static class WhatsAppTestConversation
{
    private const string ProfileName = "Ana Pérez";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<string> ReceiveBusinessAsync(ApiFactory factory, HttpClient client, PhoneNumber phone)
    {
        var bsuid = MetaWebhook.UniqueBsuid();
        var waId = phone.Value[1..];
        await PostAsync(client, waId, bsuid, MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), waId, bsuid, Now(factory), "Hola"));
        await ProcessPendingAsync(factory);
        return bsuid;
    }

    /// <summary>
    /// Recibe y procesa sin respuesta un mensaje; prepara explícitamente un vínculo histórico para comprobar el
    /// perfil y la administración. Este fixture no representa un efecto del ingreso de mensajes.
    /// </summary>
    public static async Task<string> ArrangeHistoricalLinkedContactAsync(ApiFactory factory, HttpClient client, PhoneNumber phone, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(phone);

        var bsuid = MetaWebhook.UniqueBsuid();
        var waId = phone.Value[1..];

        await PostAsync(client, waId, bsuid, MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), waId, bsuid, Now(factory), "Hola"));
        await ProcessPendingAsync(factory);
        await factory.InTransactionAsync(async services =>
        {
            var contact = await services.GetRequiredService<IWhatsAppContactRepository>().GetByUserIdentifierAsync(bsuid, Ct);
            contact!.LinkUser(userId);
        });

        return bsuid;
    }

    /// <summary>
    /// La persona toca «Quiero entrar» en la plantilla de la invitación (un botón de plantilla con el payload
    /// <c>WANT_TO_ENTER</c>) y el bot le contesta. Devuelve el BSUID del contacto.
    /// </summary>
    public static async Task<string> TapWantToEnterAsync(ApiFactory factory, HttpClient client, PhoneNumber phone)
    {
        ArgumentNullException.ThrowIfNull(phone);

        var bsuid = MetaWebhook.UniqueBsuid();
        var waId = phone.Value[1..];

        await PostAsync(
            client,
            waId,
            bsuid,
            MetaWebhook.TemplateButton(MetaWebhook.UniqueWaMessageId(), waId, bsuid, Now(factory), "WANT_TO_ENTER", "Quiero entrar"));
        await ProcessPendingAsync(factory);

        return bsuid;
    }

    /// <summary>
    /// Un aviso de estado de Meta sobre un mensaje que mandó el bot. Un "failed" puede traer el código de error de Meta
    /// (<paramref name="errorCode"/>).
    /// </summary>
    public static async Task PostStatusAsync(
        HttpClient client,
        string waMessageId,
        string status,
        DateTime atUtc,
        PhoneNumber to,
        int? errorCode = null)
    {
        ArgumentNullException.ThrowIfNull(to);

        await PostSignedAsync(
            client, MetaWebhook.Build(statuses: [MetaWebhook.Status(waMessageId, status, atUtc, to.Value[1..], errorCode)]));
    }

    public static Task<WhatsAppContact> ContactAsync(ApiFactory factory, string bsuid)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return factory.ExecuteDbContextAsync(db =>
            db.Set<WhatsAppContact>().AsNoTracking().SingleAsync(contact => contact.UserIdentifier == bsuid, Ct));
    }

    private static DateTime Now(ApiFactory factory) => MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);

    private static Task ProcessPendingAsync(ApiFactory factory) =>
        factory.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

    private static Task PostAsync(HttpClient client, string waId, string bsuid, JsonObject message) =>
        PostSignedAsync(client, MetaWebhook.Build(contacts: [MetaWebhook.Contact(waId, bsuid, ProfileName)], messages: [message]));

    private static async Task PostSignedAsync(HttpClient client, byte[] body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/whatsapp", UriKind.Relative))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));

        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
