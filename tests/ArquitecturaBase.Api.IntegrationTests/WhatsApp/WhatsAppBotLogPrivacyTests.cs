using System.Net;
using System.Net.Http.Headers;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Application.Features.WhatsApp.HandleInboundMessage;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// Ningún log del bot, del procesador ni de la cola de salida deja ver el texto de un mensaje, el nombre de perfil, el
/// enlace, su token, un BSUID ni un número completo (reglas del plan): los números van enmascarados. Todos los niveles
/// de log quedan prendidos menos los de ASP.NET Core, como en <see cref="WhatsAppWebhookLogPrivacyTests"/>.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppBotLogPrivacyTests(ApiFactory factory)
{
    private const string Text = "Texto-privado-4417";
    private const string ProfileName = "Nombre Privado Bot";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_log_of_the_bot_or_the_sender_carries_texts_links_tokens_bsuids_or_numbers()
    {
        string[] sentIds = [MetaWebhook.UniqueWaMessageId(), MetaWebhook.UniqueWaMessageId()];
        var meta = new FakeMetaHandler(call => FakeMetaHandler.Success(sentIds[call - 1]));
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Logging:LogLevel:Default", "Trace")
            .ConfigureLogging(logging => logging.AddFakeLogging())
            .ConfigureTestServices(services =>
                services.AddHttpClient(WhatsAppRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta)));
        using var client = api.CreateClient();
        var now = MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);

        // Una persona con cuenta (le llega el enlace), otra sin cuenta que la crea desde el chat, y un mensaje de hace
        // más de 24 horas (queda sin respuesta).
        var member = Sender.Unique();
        var newcomer = Sender.Unique();
        var late = Sender.Unique();
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateAsync(email: null, member.Phone, phoneConfirmed: false, ProfileName, "es", Ct));

        await PostSignedAsync(client, member.Webhook(MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), member.WaId, member.Bsuid, now, Text)));
        await PostSignedAsync(client, newcomer.Webhook(MetaWebhook.ButtonReply(
            MetaWebhook.UniqueWaMessageId(), newcomer.WaId, newcomer.Bsuid, now, BotButtons.CreateAccount, "Crear cuenta")));
        await PostSignedAsync(client, late.Webhook(MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), late.WaId, late.Bsuid, now.AddDays(-2), Text)));
        await api.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

        // En el arnés las respuestas quedan en memoria: se pasan a la cola de verdad, que las manda a la Graph API de
        // mentira y las guarda en el historial.
        var replies = factory.WhatsApp.SentTo(member.Phone).Concat(factory.WhatsApp.SentTo(newcomer.Phone)).ToList();
        Assert.Equal(2, replies.Count);
        var outbox = api.Services.GetRequiredService<WhatsAppOutbox>();
        Assert.All(replies, reply => Assert.True(outbox.TryEnqueue(reply)));
        await WaitUntilAsync(async () => await factory.ExecuteDbContextAsync(db =>
            db.WhatsAppMessages.CountAsync(message => sentIds.Contains(message.WaMessageId), Ct)) == 2);

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

        // Que el log se haya capturado de verdad: el bot contestó, creó una cuenta y la cola mandó, con el número tapado.
        Assert.Contains(records, record => record.Message.StartsWith("The WhatsApp bot answered", StringComparison.Ordinal));
        Assert.Contains(records, record => record.Message.StartsWith("The WhatsApp bot created an account", StringComparison.Ordinal));
        Assert.Contains(records, record => record.Message.StartsWith("The WhatsApp bot marked", StringComparison.Ordinal));
        Assert.Contains(records, record => record.Level == LogLevel.Information
            && record.Message.Contains("•••• " + member.WaId[^4..] + " was sent", StringComparison.Ordinal));

        var links = replies.OfType<WhatsAppLinkButtonMessage>().Select(reply => reply.Url).ToList();
        Assert.Equal(2, links.Count);
        string[] secrets =
        [
            Text,
            ProfileName,
            .. new[] { member, newcomer, late }.SelectMany(sender => new[] { sender.Bsuid, sender.WaId, sender.Phone.Value }),
            .. links,
            .. links.Select(link => link[(link.IndexOf("#t=", StringComparison.Ordinal) + "#t=".Length)..]),
        ];
        var leaks = secrets
            .SelectMany(secret => logged.Where(text => text.Contains(secret, StringComparison.Ordinal)).Select(text => secret + " in: " + text))
            .ToList();

        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine + "---" + Environment.NewLine, leaks));
    }

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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!await condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed record Sender(string WaId, string Bsuid)
    {
        public PhoneNumber Phone => PhoneNumber.Create("+" + WaId).Value;

        public static Sender Unique() => new(MetaWebhook.UniqueWaId(), MetaWebhook.UniqueBsuid());

        public byte[] Webhook(System.Text.Json.Nodes.JsonObject message) =>
            MetaWebhook.Build(contacts: [MetaWebhook.Contact(WaId, Bsuid, ProfileName)], messages: [message]);
    }
}
