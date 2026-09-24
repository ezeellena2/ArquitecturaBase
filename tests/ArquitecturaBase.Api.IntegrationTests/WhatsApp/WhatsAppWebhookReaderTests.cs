using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// Cómo se lee el formato de Meta (referencia del webhook <c>messages</c>). Son de unidad: no usan la base.
/// </summary>
public sealed class WhatsAppWebhookReaderTests
{
    private const string WaId = "5493413654813";
    private const string Bsuid = "AR.1102953142229032";
    private const string WaMessageId = "wamid.HBgNNTQ5MzQxMzY1NDgxMxUCABIYFDNBMzQ2RkY5MkJCMUM3RkUzQTg0AA==";

    private static readonly DateTime SentAtUtc = new(2026, 9, 22, 18, 30, 15, DateTimeKind.Utc);

    private readonly WhatsAppWebhookReader _reader = new(
        Options.Create(new WhatsAppOptions { PhoneNumberId = ApiFactory.WhatsAppPhoneNumberId, AccessToken = "test-access-token" }),
        NullLogger<WhatsAppWebhookReader>.Instance);

    [Fact]
    public void A_text_message_comes_with_its_contact()
    {
        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana Pérez")],
            messages: [MetaWebhook.Text(WaMessageId, WaId, Bsuid, SentAtUtc, "Hola")]));

        var message = Assert.Single(batch.Messages);
        Assert.Equal(WaMessageId, message.WaMessageId);
        Assert.Equal(new WhatsAppWebhookContact(WaId, Bsuid, "Ana Pérez"), message.From);
        Assert.Equal(WhatsAppMessageKind.Text, message.Kind);
        Assert.Equal("Hola", message.Body);
        Assert.Null(message.ReplyId);
        Assert.Equal(SentAtUtc, message.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, message.OccurredAtUtc.Kind);
        Assert.Empty(batch.Statuses);
    }

    [Fact]
    public void A_reply_button_keeps_its_id_and_its_title()
    {
        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")],
            messages: [MetaWebhook.ButtonReply(WaMessageId, WaId, Bsuid, SentAtUtc, "CREATE_ACCOUNT", "Crear cuenta")]));

        var message = Assert.Single(batch.Messages);
        Assert.Equal(WhatsAppMessageKind.ButtonReply, message.Kind);
        Assert.Equal("Crear cuenta", message.Body);
        Assert.Equal("CREATE_ACCOUNT", message.ReplyId);
    }

    /// <summary>El botón "Quiero entrar" de la plantilla de invitación llega como <c>button</c>, con su payload.</summary>
    [Fact]
    public void A_template_button_keeps_its_payload_as_the_reply_id()
    {
        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")],
            messages: [MetaWebhook.TemplateButton(WaMessageId, WaId, Bsuid, SentAtUtc, "WANT_TO_ENTER", "Quiero entrar")]));

        var message = Assert.Single(batch.Messages);
        Assert.Equal(WhatsAppMessageKind.ButtonReply, message.Kind);
        Assert.Equal("Quiero entrar", message.Body);
        Assert.Equal("WANT_TO_ENTER", message.ReplyId);
    }

    [Fact]
    public void A_photo_is_media_without_a_body()
    {
        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")],
            messages: [MetaWebhook.Image(WaMessageId, WaId, Bsuid, SentAtUtc)]));

        var message = Assert.Single(batch.Messages);
        Assert.Equal(WhatsAppMessageKind.Media, message.Kind);
        Assert.Null(message.Body);
    }

    [Theory]
    [InlineData("audio")]
    [InlineData("video")]
    [InlineData("document")]
    [InlineData("sticker")]
    public void Audio_video_documents_and_stickers_are_media(string type)
    {
        var message = MetaWebhook.Text(WaMessageId, WaId, Bsuid, SentAtUtc, "x");
        message.Remove("text");
        message["type"] = type;
        message[type] = new JsonObject { ["id"] = "1003383421387256", ["mime_type"] = "application/octet-stream" };

        var batch = Read(MetaWebhook.Build(contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")], messages: [message]));

        Assert.Equal(WhatsAppMessageKind.Media, Assert.Single(batch.Messages).Kind);
    }

    /// <summary>El aviso de cambio de número no trae contactos: la persona es la del <c>from</c>.</summary>
    [Fact]
    public void A_number_change_is_a_system_message_from_the_old_number()
    {
        var batch = Read(MetaWebhook.Build(messages: [MetaWebhook.NumberChanged(WaMessageId, WaId, SentAtUtc, "5493415550000")]));

        var message = Assert.Single(batch.Messages);
        Assert.Equal(WhatsAppMessageKind.System, message.Kind);
        Assert.Equal(new WhatsAppWebhookContact(WaId, null, null), message.From);
        Assert.Contains("5493415550000", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_that_is_not_known_is_other_without_a_body()
    {
        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")],
            messages: [MetaWebhook.Reaction(WaMessageId, WaId, Bsuid, SentAtUtc)]));

        var message = Assert.Single(batch.Messages);
        Assert.Equal(WhatsAppMessageKind.Other, message.Kind);
        Assert.Null(message.Body);
        Assert.Null(message.ReplyId);
    }

    /// <summary>Cuando la persona adopta un nombre de usuario, <c>wa_id</c> y <c>from</c> pueden llegar vacíos.</summary>
    [Fact]
    public void A_person_without_a_number_comes_only_with_the_bsuid()
    {
        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(waId: null, Bsuid, "Ana")],
            messages: [MetaWebhook.Text(WaMessageId, from: null, Bsuid, SentAtUtc, "Hola")]));

        Assert.Equal(new WhatsAppWebhookContact(null, Bsuid, "Ana"), Assert.Single(batch.Messages).From);
    }

    /// <summary>Antes de los BSUID, un webhook traía solo el número.</summary>
    [Fact]
    public void A_person_without_a_bsuid_comes_only_with_the_number()
    {
        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(WaId, userIdentifier: null, "Ana")],
            messages: [MetaWebhook.Text(WaMessageId, WaId, fromUserIdentifier: null, SentAtUtc, "Hola")]));

        Assert.Equal(new WhatsAppWebhookContact(WaId, null, "Ana"), Assert.Single(batch.Messages).From);
    }

    [Fact]
    public void Each_message_gets_the_name_of_its_own_contact()
    {
        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact("5493415550000", "AR.2", "Beto"), MetaWebhook.Contact(WaId, Bsuid, "Ana")],
            messages:
            [
                MetaWebhook.Text("wamid.1", WaId, Bsuid, SentAtUtc, "Soy Ana"),
                MetaWebhook.Text("wamid.2", "5493415550000", fromUserIdentifier: null, SentAtUtc, "Soy Beto"),
            ]));

        Assert.Equal(["Ana", "Beto"], batch.Messages.Select(message => message.From.ProfileName));
        Assert.Equal("AR.2", batch.Messages[1].From.UserIdentifier);
    }

    [Fact]
    public void Statuses_come_with_their_time_and_the_error_code_of_a_failure()
    {
        var batch = Read(MetaWebhook.Build(statuses:
        [
            MetaWebhook.Status("wamid.a", "sent", SentAtUtc),
            MetaWebhook.Status("wamid.a", "delivered", SentAtUtc.AddSeconds(2)),
            MetaWebhook.Status("wamid.a", "read", SentAtUtc.AddSeconds(9)),
            MetaWebhook.Status("wamid.b", "failed", SentAtUtc.AddSeconds(3), errorCode: 131026),
        ]));

        Assert.Empty(batch.Messages);
        Assert.Equal(
            [
                new WhatsAppWebhookStatus("wamid.a", WhatsAppMessageStatus.Sent, SentAtUtc, null),
                new WhatsAppWebhookStatus("wamid.a", WhatsAppMessageStatus.Delivered, SentAtUtc.AddSeconds(2), null),
                new WhatsAppWebhookStatus("wamid.a", WhatsAppMessageStatus.Read, SentAtUtc.AddSeconds(9), null),
                new WhatsAppWebhookStatus("wamid.b", WhatsAppMessageStatus.Failed, SentAtUtc.AddSeconds(3), 131026),
            ],
            batch.Statuses);
    }

    /// <summary>"played" es de los mensajes de voz, que el bot no manda.</summary>
    [Fact]
    public void A_status_that_is_not_known_is_left_out()
    {
        var batch = Read(MetaWebhook.Build(statuses: [MetaWebhook.Status("wamid.a", "played", SentAtUtc)]));

        Assert.True(batch.IsEmpty);
    }

    [Fact]
    public void Another_phone_number_is_left_out()
    {
        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")],
            messages: [MetaWebhook.Text(WaMessageId, WaId, Bsuid, SentAtUtc, "Hola")],
            phoneNumberId: "123456123"));

        Assert.True(batch.IsEmpty);
    }

    [Fact]
    public void Another_object_or_another_field_is_left_out()
    {
        var otherObject = Read(MetaWebhook.Build(
            messages: [MetaWebhook.Text(WaMessageId, WaId, Bsuid, SentAtUtc, "Hola")], objectName: "page"));
        var otherField = Read(MetaWebhook.Build(
            messages: [MetaWebhook.Text(WaMessageId, WaId, Bsuid, SentAtUtc, "Hola")], field: "smb_message_echoes"));

        Assert.True(otherObject.IsEmpty);
        Assert.True(otherField.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"object":"whatsapp_business_account","entry":"nope"}""")]
    [InlineData("""{"object":"whatsapp_business_account","entry":[{"changes":[{"field":"messages","value":[]}]}]}""")]
    public void A_body_that_cannot_be_read_gives_an_empty_batch(string body)
    {
        Assert.True(Read(Encoding.UTF8.GetBytes(body)).IsEmpty);
    }

    [Fact]
    public void Items_that_cannot_be_read_are_left_out_and_the_rest_is_kept()
    {
        var noId = MetaWebhook.Text("wamid.x", WaId, Bsuid, SentAtUtc, "sin id");
        noId.Remove("id");
        var badTimestamp = MetaWebhook.Text("wamid.y", WaId, Bsuid, SentAtUtc, "hora rota");
        badTimestamp["timestamp"] = "ayer";
        var nobody = MetaWebhook.Text("wamid.z", from: null, fromUserIdentifier: null, SentAtUtc, "de nadie");
        var tooLongId = MetaWebhook.Text(new string('w', WhatsAppMessage.MaxWaMessageIdLength + 1), WaId, Bsuid, SentAtUtc, "id largo");
        var badStatus = MetaWebhook.Status("wamid.s", "read", SentAtUtc);
        badStatus["timestamp"] = "-";

        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")],
            messages: [noId, badTimestamp, nobody, tooLongId, MetaWebhook.Text(WaMessageId, WaId, Bsuid, SentAtUtc, "Hola")],
            statuses: [badStatus, MetaWebhook.Status("wamid.ok", "read", SentAtUtc)]));

        Assert.Equal(WaMessageId, Assert.Single(batch.Messages).WaMessageId);
        Assert.Equal("wamid.ok", Assert.Single(batch.Statuses).WaMessageId);
    }

    /// <summary>Un número que no es solo dígitos o un BSUID con espacios no se guardan: se leen como ausentes.</summary>
    [Fact]
    public void Identifiers_with_an_unexpected_shape_are_read_as_missing()
    {
        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact("+54 9 341", "AR 11", "Ana")],
            messages: [MetaWebhook.Text(WaMessageId, "+54 9 341", "AR 11", SentAtUtc, "Hola")]));

        Assert.Empty(batch.Messages);
    }

    /// <summary>
    /// Meta documenta los BSUID con "hasta 256 caracteres alfanuméricos". Justo quien no manda el número (un nombre de
    /// usuario, fuera de los 30 días) llega solo con el BSUID: si no entrara, su mensaje se perdería.
    /// </summary>
    [Fact]
    public void A_bsuid_of_256_characters_is_read()
    {
        var bsuid = "AR." + new string('7', 253);

        var batch = Read(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(waId: null, bsuid, "Ana")],
            messages: [MetaWebhook.Text(WaMessageId, from: null, bsuid, SentAtUtc, "Hola")]));

        Assert.Equal(new WhatsAppWebhookContact(null, bsuid, "Ana"), Assert.Single(batch.Messages).From);
    }

    /// <summary>
    /// JsonDocument acepta dentro de un texto bytes que no son UTF-8 válido y media letra escapada sin su pareja, y
    /// recién GetString los rechaza con una excepción. Ese campo se lee como ausente: un texto roto no frena el resto
    /// del webhook, y un id roto deja afuera solo su mensaje.
    /// </summary>
    [Fact]
    public void Text_that_is_not_valid_utf8_is_read_as_missing_without_stopping_the_rest()
    {
        byte[] invalidUtf8 = [0xC3, 0x28];
        var body = MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(WaId, Bsuid, "BROKEN_NAME")],
            messages:
            [
                MetaWebhook.Text("wamid.1", WaId, Bsuid, SentAtUtc, "BROKEN_TEXT"),
                MetaWebhook.Text("wamid.2", WaId, Bsuid, SentAtUtc, "LOOSE_HALF"),
                MetaWebhook.Text("BROKEN_ID", WaId, Bsuid, SentAtUtc, "Hola"),
                MetaWebhook.Text(WaMessageId, WaId, Bsuid, SentAtUtc, "Hola"),
            ]);
        body = Replace(body, "\"BROKEN_NAME\"", [.. "\"Ana"u8, .. invalidUtf8, .. "\""u8]);
        body = Replace(body, "\"BROKEN_TEXT\"", [.. "\"Ho"u8, .. invalidUtf8, .. "la\""u8]);
        body = Replace(body, "\"LOOSE_HALF\"", [.. "\"\\ud800x\""u8]);
        body = Replace(body, "\"BROKEN_ID\"", [.. "\"wamid."u8, .. invalidUtf8, .. "\""u8]);

        var batch = Read(body);

        Assert.Equal(["wamid.1", "wamid.2", WaMessageId], batch.Messages.Select(message => message.WaMessageId));
        Assert.Equal([null, null, "Hola"], batch.Messages.Select(message => message.Body));
        Assert.All(batch.Messages, message => Assert.Equal(new WhatsAppWebhookContact(WaId, Bsuid, null), message.From));
    }

    /// <summary>
    /// Lo mismo pasa con un nombre de propiedad: JsonDocument acepta uno escapado con media letra sin su pareja, y
    /// TryGetProperty lo desescapa recién al compararlo con el que busca, y ahí tira. Va al final de cada objeto que se
    /// recorre, que es por donde TryGetProperty empieza a buscar, y es más largo que cualquiera de los nombres que se
    /// buscan, que es cuando lo desescapa. No es ninguno de los que se leen: se saltea, y el resto se lee igual.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("entry/0")]
    [InlineData("entry/0/changes/0")]
    [InlineData("entry/0/changes/0/value")]
    [InlineData("entry/0/changes/0/value/metadata")]
    [InlineData("entry/0/changes/0/value/contacts/0")]
    [InlineData("entry/0/changes/0/value/contacts/0/profile")]
    [InlineData("entry/0/changes/0/value/messages/0")]
    [InlineData("entry/0/changes/0/value/messages/0/text")]
    [InlineData("entry/0/changes/0/value/messages/1/interactive")]
    [InlineData("entry/0/changes/0/value/messages/1/interactive/button_reply")]
    [InlineData("entry/0/changes/0/value/statuses/0")]
    [InlineData("entry/0/changes/0/value/statuses/0/errors/0")]
    public void A_property_name_with_a_loose_half_is_skipped_without_stopping_the_rest(string path)
    {
        var json = JsonNode.Parse(MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")],
            messages:
            [
                MetaWebhook.Text("wamid.1", WaId, Bsuid, SentAtUtc, "Hola"),
                MetaWebhook.ButtonReply("wamid.2", WaId, Bsuid, SentAtUtc, "CREATE_ACCOUNT", "Crear cuenta"),
            ],
            statuses: [MetaWebhook.Status("wamid.3", "failed", SentAtUtc, errorCode: 131026)]))!;
        ObjectAt(json, path)["BROKEN_NAME"] = 0;
        var body = Replace(
            Encoding.UTF8.GetBytes(json.ToJsonString()), "\"BROKEN_NAME\"", [.. "\"\\udc00xxxxxxxxxxxxxxxxxxxx\""u8]);

        var batch = Read(body);

        var from = new WhatsAppWebhookContact(WaId, Bsuid, "Ana");
        Assert.Equal(
            [
                new WhatsAppWebhookMessage("wamid.1", from, WhatsAppMessageKind.Text, "Hola", null, SentAtUtc),
                new WhatsAppWebhookMessage("wamid.2", from, WhatsAppMessageKind.ButtonReply, "Crear cuenta", "CREATE_ACCOUNT", SentAtUtc),
            ],
            batch.Messages);
        Assert.Equal([new WhatsAppWebhookStatus("wamid.3", WhatsAppMessageStatus.Failed, SentAtUtc, 131026)], batch.Statuses);
    }

    /// <summary>
    /// Un mensaje sin un BSUID ni un número que se puedan leer no tiene a quién asignarse. Se avisa aparte de los demás
    /// ítems que no se pudieron leer: si Meta cambiara la forma de los identificadores, es lo primero que se notaría.
    /// </summary>
    [Fact]
    public void A_message_without_a_readable_sender_leaves_its_own_warning()
    {
        var logger = new FakeLogger<WhatsAppWebhookReader>();
        var reader = new WhatsAppWebhookReader(
            Options.Create(new WhatsAppOptions { PhoneNumberId = ApiFactory.WhatsAppPhoneNumberId, AccessToken = "test-access-token" }),
            logger);
        var noId = MetaWebhook.Text("wamid.x", WaId, Bsuid, SentAtUtc, "sin id");
        noId.Remove("id");

        var batch = reader.Read(MetaWebhook.Build(messages:
        [
            noId,
            MetaWebhook.Text("wamid.y", from: null, fromUserIdentifier: null, SentAtUtc, "de nadie"),
            MetaWebhook.Text("wamid.z", from: null, "AR " + new string('7', 20), SentAtUtc, "BSUID con espacio"),
        ]));

        Assert.True(batch.IsEmpty);
        var warnings = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, record => record.Message.Contains("without a readable sender", StringComparison.Ordinal)
            && record.GetStructuredStateValue("Count") == "2");
        Assert.Contains(warnings, record => record.Message.Contains("could not be read", StringComparison.Ordinal)
            && record.GetStructuredStateValue("Count") == "1");
    }

    private WhatsAppWebhookBatch Read(byte[] body) => _reader.Read(body);

    /// <summary>Cambia un pedazo del cuerpo por bytes que un serializador no escribiría nunca.</summary>
    private static byte[] Replace(byte[] body, string placeholder, byte[] replacement)
    {
        var index = body.AsSpan().IndexOf(Encoding.UTF8.GetBytes(placeholder));
        Assert.True(index >= 0, $"The body has no {placeholder}.");

        return [.. body[..index], .. replacement, .. body[(index + Encoding.UTF8.GetByteCount(placeholder))..]];
    }

    /// <summary>El objeto al que lleva un camino como "entry/0/changes/0": los números son posiciones de un arreglo.</summary>
    private static JsonObject ObjectAt(JsonNode root, string path)
    {
        var node = root;

        foreach (var step in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            node = int.TryParse(step, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? node[index]! : node[step]!;
        }

        return node.AsObject();
    }
}
