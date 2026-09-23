using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// Ningún log del webhook tiene el cuerpo, el texto de un mensaje, el nombre de la persona, el secreto de la app, la
/// palabra de verificación, la firma, un BSUID ni un número completo (reglas del plan). Todos los niveles de log
/// quedan prendidos menos los de ASP.NET Core, que quedan como en appsettings.json: su línea "Request starting"
/// incluye la query string, y en el GET de verificación Meta manda la palabra en la query.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppWebhookLogPrivacyTests(ApiFactory factory)
{
    private const string Route = "/webhooks/whatsapp";
    private const string Text = "Texto-privado-7781";
    private const string ProfileName = "Nombre Privado";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_log_carries_the_body_the_text_the_secrets_the_signature_the_bsuid_or_the_number()
    {
        var waId = MetaWebhook.UniqueWaId();
        var bsuid = MetaWebhook.UniqueBsuid();
        var now = MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);
        var signed = MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(waId, bsuid, ProfileName)],
            messages:
            [
                MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), waId, bsuid, now, Text),
                MetaWebhook.NumberChanged(MetaWebhook.UniqueWaMessageId(), waId, now, "5493415550000"),
            ],
            statuses: [MetaWebhook.Status(MetaWebhook.UniqueWaMessageId(), "failed", now, waId, errorCode: 131026)]);
        var otherNumber = MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(waId, bsuid, ProfileName)],
            messages: [MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), waId, bsuid, now, Text)],
            phoneNumberId: "123456123");
        var unreadable = Encoding.UTF8.GetBytes("{\"text\":\"" + Text + "\"");

        // Otra persona, cuyo mensaje ya está guardado, vuelve a llegar con uno nuevo, y el lote choca con el índice
        // único: es el único camino en el que Postgres podría poner los valores de una fila en una excepción.
        var collidingWaId = MetaWebhook.UniqueWaId();
        var collidingBsuid = MetaWebhook.UniqueBsuid();
        var alreadySaved = MetaWebhook.UniqueWaMessageId();
        await SaveInboundAsync(collidingWaId, collidingBsuid, alreadySaved, now);
        var colliding = MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(collidingWaId, collidingBsuid, ProfileName)],
            messages:
            [
                MetaWebhook.Text(alreadySaved, collidingWaId, collidingBsuid, now, Text),
                MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), collidingWaId, collidingBsuid, now.AddSeconds(1), Text),
            ]);

        // La firma de un secreto que no es el de la Api: la de Meta cuando el secreto está mal cargado. Es la que más
        // tienta registrar para depurar un rechazo.
        var rejectedSignature = MetaWebhook.Sign(signed, "another-secret");

        var staleReads = new StaleReads(alreadySaved);
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Logging:LogLevel:Default", "Trace")
            .ConfigureLogging(logging => logging.AddFakeLogging())
            .ConfigureTestServices(services => StaleReadsMessageRepository.Replace(services, staleReads)));
        using var client = api.CreateClient();

        using var verification = await client.GetAsync(
            new Uri($"{Route}?hub.mode=subscribe&hub.verify_token={ApiFactory.WhatsAppVerifyToken}&hub.challenge=42", UriKind.Relative), Ct);
        using var wrongVerification = await client.GetAsync(
            new Uri($"{Route}?hub.mode=subscribe&hub.verify_token={ApiFactory.WhatsAppVerifyToken}x&hub.challenge=42", UriKind.Relative), Ct);
        using var accepted = await PostAsync(client, signed, MetaWebhook.Sign(signed));
        using var repeated = await PostAsync(client, signed, MetaWebhook.Sign(signed));
        using var rejected = await PostAsync(client, signed, rejectedSignature);
        using var ignored = await PostAsync(client, otherNumber, MetaWebhook.Sign(otherNumber));
        using var broken = await PostAsync(client, unreadable, MetaWebhook.Sign(unreadable));
        using var collided = await PostAsync(client, colliding, MetaWebhook.Sign(colliding));

        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongVerification.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);
        Assert.Equal(HttpStatusCode.OK, broken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, collided.StatusCode);
        Assert.True(staleReads.Used);

        var records = api.Services.GetFakeLogCollector().GetSnapshot();
        var logged = records
            .Select(record => string.Join(
                "\n",
                [
                    record.Category ?? string.Empty,
                    record.Message,
                    record.Exception?.ToString() ?? string.Empty,
                    .. record.StructuredState?.Select(pair => pair.Value ?? string.Empty) ?? [],
                    .. record.Scopes.Select(scope => scope?.ToString() ?? string.Empty),
                ]))
            .ToList();

        // Que el log se haya capturado de verdad: el guardado, el rechazo, lo ignorado y el choque dejan su línea.
        Assert.Contains(records, record => record.Level == LogLevel.Information && record.Message.Contains("Received a WhatsApp webhook", StringComparison.Ordinal));
        Assert.Contains(records, record => record.Level == LogLevel.Warning && record.Message.Contains("signature", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(records, record => record.Message.Contains("ignored", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(records, record => record.Message.StartsWith("Another request saved part of a WhatsApp webhook", StringComparison.Ordinal));

        var signature = MetaWebhook.Sign(signed);
        var collidingSignature = MetaWebhook.Sign(colliding);
        string[] secrets =
        [
            Text,
            ProfileName,
            bsuid,
            waId,
            "+" + waId,
            collidingBsuid,
            collidingWaId,
            "+" + collidingWaId,
            ApiFactory.WhatsAppAppSecret,
            ApiFactory.WhatsAppVerifyToken,
            signature,
            signature["sha256=".Length..],
            rejectedSignature,
            rejectedSignature["sha256=".Length..],
            collidingSignature,
            collidingSignature["sha256=".Length..],
            Encoding.UTF8.GetString(signed),
            Encoding.UTF8.GetString(colliding),
        ];
        var leaks = secrets
            .SelectMany(secret => logged.Where(text => text.Contains(secret, StringComparison.Ordinal)).Select(text => secret + " in: " + text))
            .ToList();

        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine + "---" + Environment.NewLine, leaks));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, byte[] body, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Route, UriKind.Relative))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hub-Signature-256", signature);

        return await client.SendAsync(request, Ct);
    }

    private Task<int> SaveInboundAsync(string waId, string bsuid, string waMessageId, DateTime atUtc) =>
        factory.ExecuteDbContextAsync(db =>
        {
            var contact = WhatsAppContact.Create(waId, bsuid, ProfileName, atUtc);
            db.WhatsAppContacts.Add(contact);
            db.WhatsAppMessages.Add(WhatsAppMessage.Inbound(contact.Id, waMessageId, WhatsAppMessageKind.Text, Text, null, atUtc));

            return db.SaveChangesAsync(Ct);
        });
}
