using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// <c>/webhooks/whatsapp</c> (sección 7 del spec del ingreso con WhatsApp): la verificación de Meta, la firma, el
/// guardado de contactos, mensajes y estados, los duplicados y los límites. Los webhooks tienen la forma de la
/// documentación de Meta y se firman con el secreto de la app de los tests.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppWebhookTests(ApiFactory factory)
{
    private const string Route = "/webhooks/whatsapp";
    private const string SignatureHeader = "X-Hub-Signature-256";

    /// <summary>El límite del cuerpo: Meta agrupa hasta 1000 novedades en un webhook.</summary>
    private const int MaxBodyBytes = 5 * 1024 * 1024;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);

    [Fact]
    public async Task Meta_verification_gets_the_challenge_back_in_plain_text()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"{Route}?hub.mode=subscribe&hub.verify_token={ApiFactory.WhatsAppVerifyToken}&hub.challenge=1158201444", UriKind.Relative),
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("1158201444", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("hub.mode=subscribe&hub.verify_token=wrong-token&hub.challenge=1158201444")]
    [InlineData("hub.mode=unsubscribe&hub.verify_token=" + ApiFactory.WhatsAppVerifyToken + "&hub.challenge=1158201444")]
    [InlineData("hub.verify_token=" + ApiFactory.WhatsAppVerifyToken + "&hub.challenge=1158201444")]
    [InlineData("hub.mode=subscribe&hub.challenge=1158201444")]
    [InlineData("hub.mode=subscribe&hub.verify_token=" + ApiFactory.WhatsAppVerifyToken)]
    public async Task Verification_with_another_word_or_another_mode_is_forbidden(string query)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"{Route}?{query}", UriKind.Relative), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("1158201444", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256=")]
    [InlineData("sha256=not-hex")]
    [InlineData("sha256=00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff")]
    [InlineData("wrong-secret")]
    public async Task A_post_without_a_valid_signature_is_rejected_and_saves_nothing(string? signature)
    {
        var sender = Sender.Unique();
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var body = MetaWebhook.Build(
            contacts: [sender.Contact()],
            messages: [MetaWebhook.Text(waMessageId, sender.WaId, sender.Bsuid, Now, "Hola")]);

        using var response = await PostAsync(body, signature == "wrong-secret" ? MetaWebhook.Sign(body, "another-secret") : signature);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await FindMessageAsync(waMessageId));
        Assert.Null(await FindContactAsync(sender.Bsuid));
    }

    [Fact]
    public async Task A_signed_text_saves_the_contact_and_the_pending_message()
    {
        var sender = Sender.Unique();
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var sentAtUtc = Now.AddSeconds(-3);

        using var response = await PostSignedAsync(MetaWebhook.Build(
            contacts: [sender.Contact("Ana Pérez")],
            messages: [MetaWebhook.Text(waMessageId, sender.WaId, sender.Bsuid, sentAtUtc, "Hola, quiero entrar")]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));

        var contact = Assert.IsType<WhatsAppContact>(await FindContactAsync(sender.Bsuid));
        Assert.Equal(sender.WaId, contact.WaId);
        Assert.Equal("Ana Pérez", contact.ProfileName);
        Assert.Null(contact.UserId);
        Assert.Equal(sentAtUtc, contact.LastInboundAtUtc);

        var message = Assert.IsType<WhatsAppMessage>(await FindMessageAsync(waMessageId));
        Assert.Equal(contact.Id, message.ContactId);
        Assert.Equal(WhatsAppMessageDirection.Inbound, message.Direction);
        Assert.Equal(WhatsAppMessageKind.Text, message.Kind);
        Assert.Equal("Hola, quiero entrar", message.Body);
        Assert.Equal(sentAtUtc, message.OccurredAtUtc);
        Assert.Null(message.ProcessedAtUtc);
    }

    [Fact]
    public async Task A_signed_button_saves_the_id_of_the_button()
    {
        var sender = Sender.Unique();
        var waMessageId = MetaWebhook.UniqueWaMessageId();

        using var response = await PostSignedAsync(MetaWebhook.Build(
            contacts: [sender.Contact()],
            messages: [MetaWebhook.ButtonReply(waMessageId, sender.WaId, sender.Bsuid, Now, "CREATE_ACCOUNT", "Crear cuenta")]));

        var message = Assert.IsType<WhatsAppMessage>(await FindMessageAsync(waMessageId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(WhatsAppMessageKind.ButtonReply, message.Kind);
        Assert.Equal("Crear cuenta", message.Body);
        Assert.Equal("CREATE_ACCOUNT", message.ReplyId);
        Assert.Null(message.ProcessedAtUtc);
    }

    /// <summary>
    /// WhatsApp deja mandar textos mucho más largos que lo que se guarda. Si el corte partiera un emoji, o si se
    /// guardara un carácter nulo, Postgres rechazaría el lote entero y Meta lo reintentaría durante días con el mismo
    /// 500: el texto se guarda limpio y el resto del lote también.
    /// </summary>
    [Fact]
    public async Task A_long_text_with_an_emoji_at_the_cut_and_null_characters_is_saved_clean()
    {
        var sender = Sender.Unique();
        var longText = MetaWebhook.UniqueWaMessageId();
        var withNull = MetaWebhook.UniqueWaMessageId();
        var atTheCut = new string('a', WhatsAppMessage.MaxBodyLength - 1);

        using var response = await PostSignedAsync(MetaWebhook.Build(
            contacts: [sender.Contact("Ana\0")],
            messages:
            [
                MetaWebhook.Text(longText, sender.WaId, sender.Bsuid, Now, atTheCut + "😀 y sigue"),
                MetaWebhook.Text(withNull, sender.WaId, sender.Bsuid, Now.AddSeconds(1), "Ho\0la"),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(atTheCut, (await FindMessageAsync(longText))?.Body);
        Assert.Equal("Hola", (await FindMessageAsync(withNull))?.Body);
        Assert.Equal("Ana", (await FindContactAsync(sender.Bsuid))?.ProfileName);
    }

    /// <summary>
    /// Meta documenta los BSUID con "hasta 256 caracteres alfanuméricos", y quien adoptó un nombre de usuario puede
    /// llegar sin número: la columna tiene que alcanzar para el más largo.
    /// </summary>
    [Fact]
    public async Task A_person_known_only_by_a_bsuid_of_256_characters_is_saved()
    {
        var bsuid = MetaWebhook.UniqueBsuid().PadRight(256, '7');
        var waMessageId = MetaWebhook.UniqueWaMessageId();

        using var response = await PostSignedAsync(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(waId: null, bsuid, "Ana")],
            messages: [MetaWebhook.Text(waMessageId, from: null, bsuid, Now, "Hola")]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contact = Assert.IsType<WhatsAppContact>(await FindContactAsync(bsuid));
        Assert.Null(contact.WaId);
        Assert.Equal(contact.Id, (await FindMessageAsync(waMessageId))?.ContactId);
    }

    /// <summary>Meta reintenta el webhook que no recibió su 200: el mismo mensaje queda una sola vez.</summary>
    [Fact]
    public async Task The_same_message_twice_is_saved_once()
    {
        var sender = Sender.Unique();
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var body = MetaWebhook.Build(
            contacts: [sender.Contact()],
            messages: [MetaWebhook.Text(waMessageId, sender.WaId, sender.Bsuid, Now, "Hola")]);

        using var first = await PostSignedAsync(body);
        using var second = await PostSignedAsync(body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, await CountMessagesAsync(waMessageId));
        Assert.Equal(1, await CountContactsAsync(sender));
    }

    /// <summary>
    /// El reintento de Meta puede cruzarse con el original. El lock por contacto los pone en fila: el segundo ve lo que
    /// guardó el primero y ninguno choca con el índice único. Procesar otra vez después de un choque es la red por si
    /// algo se escapa del lock, no el camino de todos los días: acá no tiene que pasar.
    /// </summary>
    [Fact]
    public async Task The_same_message_at_the_same_time_is_saved_once_without_colliding()
    {
        var sender = Sender.Unique();
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var body = MetaWebhook.Build(
            contacts: [sender.Contact()],
            messages: [MetaWebhook.Text(waMessageId, sender.WaId, sender.Bsuid, Now, "Hola")]);
        await using var api = WithLogs();
        using var client = api.CreateClient();

        var statuses = await StatusesOfParallelAsync(Enumerable.Range(0, 6).Select(_ => PostAsync(client, body, MetaWebhook.Sign(body))));

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(1, await CountMessagesAsync(waMessageId));
        Assert.Equal(1, await CountContactsAsync(sender));
        Assert.Empty(Collisions(api));
    }

    /// <summary>Mensajes distintos de una persona nueva, a la vez: un solo contacto, y otra vez sin choques.</summary>
    [Fact]
    public async Task Different_messages_of_a_new_person_at_the_same_time_create_a_single_contact()
    {
        var sender = Sender.Unique();
        var waMessageIds = Enumerable.Range(0, 5).Select(_ => MetaWebhook.UniqueWaMessageId()).ToList();
        await using var api = WithLogs();
        using var client = api.CreateClient();

        var statuses = await StatusesOfParallelAsync(waMessageIds
            .Select(waMessageId => MetaWebhook.Build(
                contacts: [sender.Contact()],
                messages: [MetaWebhook.Text(waMessageId, sender.WaId, sender.Bsuid, Now, "Hola")]))
            .Select(body => PostAsync(client, body, MetaWebhook.Sign(body))));

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(1, await CountContactsAsync(sender));
        Assert.Equal(5, await factory.ExecuteDbContextAsync(db => db.WhatsAppMessages.CountAsync(message => waMessageIds.Contains(message.WaMessageId), Ct)));
        Assert.Empty(Collisions(api));
    }

    [Fact]
    public async Task A_person_who_writes_again_is_the_same_contact_with_a_newer_last_inbound()
    {
        var sender = Sender.Unique();
        await PostSignedAsync(MetaWebhook.Build(
            contacts: [sender.Contact("Ana")],
            messages: [MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), sender.WaId, sender.Bsuid, Now.AddHours(-2), "Hola")]));

        using var response = await PostSignedAsync(MetaWebhook.Build(
            contacts: [sender.Contact("Ana Pérez")],
            messages: [MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), sender.WaId, sender.Bsuid, Now, "Hola de nuevo")]));

        var contact = Assert.IsType<WhatsAppContact>(await FindContactAsync(sender.Bsuid));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountContactsAsync(sender));
        Assert.Equal(Now, contact.LastInboundAtUtc);
        Assert.Equal("Ana Pérez", contact.ProfileName);
        Assert.Equal(2, await factory.ExecuteDbContextAsync(db => db.WhatsAppMessages.CountAsync(message => message.ContactId == contact.Id, Ct)));
    }

    /// <summary>Meta manda los estados desordenados: un "delivered" viejo que llega después de un "read" no lo pisa.</summary>
    [Fact]
    public async Task Statuses_update_the_outbound_message_only_when_they_are_newer()
    {
        var waMessageId = await AddOutboundAsync();

        using var read = await PostSignedAsync(MetaWebhook.Build(statuses: [MetaWebhook.Status(waMessageId, "read", Now.AddSeconds(9))]));
        using var delivered = await PostSignedAsync(MetaWebhook.Build(statuses: [MetaWebhook.Status(waMessageId, "delivered", Now.AddSeconds(5))]));

        var message = Assert.IsType<WhatsAppMessage>(await FindMessageAsync(waMessageId));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
        Assert.Equal(WhatsAppMessageStatus.Read, message.Status);
        Assert.Equal(Now.AddSeconds(9), message.StatusAtUtc);
    }

    [Fact]
    public async Task A_failure_keeps_the_error_code_of_meta()
    {
        var waMessageId = await AddOutboundAsync();

        using var response = await PostSignedAsync(MetaWebhook.Build(statuses:
        [
            MetaWebhook.Status(waMessageId, "sent", Now),
            MetaWebhook.Status(waMessageId, "failed", Now.AddSeconds(1), errorCode: 131026),
        ]));

        var message = Assert.IsType<WhatsAppMessage>(await FindMessageAsync(waMessageId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(WhatsAppMessageStatus.Failed, message.Status);
        Assert.Equal(131026, message.ErrorCode);
    }

    [Fact]
    public async Task A_status_of_a_message_that_is_not_stored_is_ignored()
    {
        var waMessageId = MetaWebhook.UniqueWaMessageId();

        using var response = await PostSignedAsync(MetaWebhook.Build(statuses: [MetaWebhook.Status(waMessageId, "delivered", Now)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindMessageAsync(waMessageId));
    }

    /// <summary>Los webhooks de prueba del panel de Meta traen su propio <c>phone_number_id</c>: responden 200 y no guardan nada.</summary>
    [Fact]
    public async Task Another_phone_number_id_is_ignored()
    {
        var sender = Sender.Unique();
        var waMessageId = MetaWebhook.UniqueWaMessageId();

        using var response = await PostSignedAsync(MetaWebhook.Build(
            contacts: [sender.Contact()],
            messages: [MetaWebhook.Text(waMessageId, sender.WaId, sender.Bsuid, Now, "Hola")],
            phoneNumberId: "123456123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindMessageAsync(waMessageId));
        Assert.Null(await FindContactAsync(sender.Bsuid));
    }

    [Fact]
    public async Task A_body_over_5_mb_is_too_large()
    {
        var body = new byte[MaxBodyBytes + 1];

        using var response = await PostAsync(body, MetaWebhook.Sign(body));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    /// <summary>Sin Content-Length (por partes), el límite se controla mientras se lee.</summary>
    [Fact]
    public async Task A_body_over_5_mb_without_a_length_is_too_large_too()
    {
        var body = new byte[MaxBodyBytes + 1];
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Route, UriKind.Relative))
        {
            Content = new StreamContent(new UnknownLengthStream(body)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(SignatureHeader, MetaWebhook.Sign(body));

        using var response = await client.SendAsync(request, Ct);

        Assert.Null(request.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task A_body_of_exactly_5_mb_is_read()
    {
        var sender = Sender.Unique();
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var webhook = MetaWebhook.Build(
            contacts: [sender.Contact()],
            messages: [MetaWebhook.Text(waMessageId, sender.WaId, sender.Bsuid, Now, "Hola")]);

        // JSON válido de 5 MB justos: el webhook, completado con espacios al final.
        var body = new byte[MaxBodyBytes];
        webhook.CopyTo(body, 0);
        Array.Fill(body, (byte)' ', webhook.Length, MaxBodyBytes - webhook.Length);

        using var response = await PostAsync(body, MetaWebhook.Sign(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await FindMessageAsync(waMessageId));
    }

    /// <summary>
    /// Si otro pedido guarda el mismo mensaje entre que este mira y guarda (una violación del índice único), el
    /// webhook se procesa otra vez: lo repetido se saltea y lo nuevo del mismo lote se guarda. Meta recibe su 200.
    /// </summary>
    [Fact]
    public async Task A_message_saved_by_another_request_at_the_last_moment_counts_as_repeated()
    {
        var sender = Sender.Unique();
        var repeated = MetaWebhook.UniqueWaMessageId();
        var fresh = MetaWebhook.UniqueWaMessageId();
        await factory.ExecuteDbContextAsync(async db =>
        {
            var contact = WhatsAppContact.Create(sender.WaId, sender.Bsuid, "Ana", Now);
            db.WhatsAppContacts.Add(contact);
            db.WhatsAppMessages.Add(WhatsAppMessage.Inbound(contact.Id, repeated, WhatsAppMessageKind.Text, "Hola", null, Now));

            return await db.SaveChangesAsync(Ct);
        });

        var staleReads = new StaleReads(repeated);
        await using var api = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddFakeLogging())
            .ConfigureTestServices(services => StaleReadsMessageRepository.Replace(services, staleReads)));
        using var client = api.CreateClient();
        var body = MetaWebhook.Build(
            contacts: [sender.Contact()],
            messages:
            [
                MetaWebhook.Text(repeated, sender.WaId, sender.Bsuid, Now, "Hola"),
                MetaWebhook.Text(fresh, sender.WaId, sender.Bsuid, Now.AddSeconds(1), "¿Hay alguien?"),
            ]);

        using var response = await PostAsync(client, body, MetaWebhook.Sign(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(staleReads.Used);
        Assert.Single(Collisions(api));
        Assert.Equal(1, await CountMessagesAsync(repeated));
        Assert.Equal(1, await CountMessagesAsync(fresh));
        Assert.Equal(1, await CountContactsAsync(sender));
    }

    /// <summary>Una política propia por IP, generosa y configurable como las demás: el arnés la relaja.</summary>
    [Fact]
    public async Task The_webhook_has_its_own_rate_limit()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:WhatsAppWebhookPermitLimit", "1"));
        using var client = api.CreateClient();
        var body = MetaWebhook.Build(phoneNumberId: "123456123");

        using var first = await PostAsync(client, body, MetaWebhook.Sign(body));
        using var second = await PostAsync(client, body, MetaWebhook.Sign(body));
        using var loginCode = await client.PostJsonAsync("/account/login-code", new { email = TestEmails.Unique("webhooklimit") });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, loginCode.StatusCode);
    }

    private Task<HttpResponseMessage> PostSignedAsync(byte[] body) => PostAsync(body, MetaWebhook.Sign(body));

    private WebApplicationFactory<Program> WithLogs() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddFakeLogging()));

    /// <summary>Las veces que un webhook chocó con el índice único y se procesó de nuevo.</summary>
    private static List<FakeLogRecord> Collisions(WebApplicationFactory<Program> api) =>
        [.. api.Services.GetFakeLogCollector().GetSnapshot()
            .Where(record => record.Message.StartsWith("Another request saved part of a WhatsApp webhook", StringComparison.Ordinal))];

    private async Task<HttpResponseMessage> PostAsync(byte[] body, string? signature)
    {
        using var client = factory.CreateClient();

        return await PostAsync(client, body, signature);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, byte[] body, string? signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Route, UriKind.Relative))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        if (signature is not null)
        {
            request.Headers.Add(SignatureHeader, signature);
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<string> AddOutboundAsync()
    {
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        await factory.ExecuteDbContextAsync(db =>
        {
            db.WhatsAppMessages.Add(WhatsAppMessage.Outbound(contactId: null, waMessageId, WhatsAppMessageKind.Text, "[código]", Now.AddSeconds(-1)));

            return db.SaveChangesAsync(Ct);
        });

        return waMessageId;
    }

    private Task<WhatsAppMessage?> FindMessageAsync(string waMessageId) =>
        factory.ExecuteDbContextAsync(db => db.WhatsAppMessages.AsNoTracking().SingleOrDefaultAsync(message => message.WaMessageId == waMessageId, Ct));

    private Task<int> CountMessagesAsync(string waMessageId) =>
        factory.ExecuteDbContextAsync(db => db.WhatsAppMessages.CountAsync(message => message.WaMessageId == waMessageId, Ct));

    private Task<WhatsAppContact?> FindContactAsync(string bsuid) =>
        factory.ExecuteDbContextAsync(db => db.WhatsAppContacts.AsNoTracking().SingleOrDefaultAsync(contact => contact.UserIdentifier == bsuid, Ct));

    private Task<int> CountContactsAsync(Sender sender) =>
        factory.ExecuteDbContextAsync(db => db.WhatsAppContacts.CountAsync(
            contact => contact.UserIdentifier == sender.Bsuid || contact.WaId == sender.WaId, Ct));

    private static async Task<HttpStatusCode[]> StatusesOfParallelAsync(IEnumerable<Task<HttpResponseMessage>> requests)
    {
        HttpResponseMessage[] responses = [];

        try
        {
            responses = await Task.WhenAll(requests);

            return [.. responses.Select(response => response.StatusCode)];
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>Una persona distinta por test: todos los tests comparten la base, y el BSUID es único.</summary>
    private sealed record Sender(string WaId, string Bsuid)
    {
        public static Sender Unique() => new(MetaWebhook.UniqueWaId(), MetaWebhook.UniqueBsuid());

        public JsonObject Contact(string profileName = "Ana") => MetaWebhook.Contact(WaId, Bsuid, profileName);
    }

    /// <summary>Un cuerpo sin largo conocido: HttpClient lo manda por partes, sin Content-Length.</summary>
    private sealed class UnknownLengthStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
