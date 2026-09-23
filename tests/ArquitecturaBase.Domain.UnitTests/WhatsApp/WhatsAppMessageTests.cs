using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.WhatsApp;

public sealed class WhatsAppMessageTests
{
    private const string WaMessageId = "wamid.HBgLMTY1MDM4Nzk0MzkVAgASGBQzQTRBNjU5OUFFRTAzODEwMTQ0RgA=";

    // Las dos mitades de un emoji y el carácter de reemplazo. Se arman en el código y no en un InlineData: los textos
    // de un atributo se guardan en UTF-8, y una mitad suelta no sobrevive el viaje.
    private const char HighSurrogate = (char)0xD83D;
    private const char LowSurrogate = (char)0xDE00;
    private const char ReplacementCharacter = (char)0xFFFD;

    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ContactId = Guid.CreateVersion7();

    [Fact]
    public void An_inbound_message_arrives_pending()
    {
        var message = WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.ButtonReply, "Crear cuenta", "CREATE_ACCOUNT", Now);

        Assert.Equal(ContactId, message.ContactId);
        Assert.Equal(WhatsAppMessageDirection.Inbound, message.Direction);
        Assert.Equal(WaMessageId, message.WaMessageId);
        Assert.Equal(WhatsAppMessageKind.ButtonReply, message.Kind);
        Assert.Equal("Crear cuenta", message.Body);
        Assert.Equal("CREATE_ACCOUNT", message.ReplyId);
        Assert.Equal(Now, message.OccurredAtUtc);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Null(message.Status);
    }

    [Fact]
    public void An_inbound_message_needs_its_contact_and_its_id()
    {
        Assert.Throws<ArgumentException>(() => WhatsAppMessage.Inbound(Guid.Empty, WaMessageId, WhatsAppMessageKind.Text, "Hola", null, Now));
        Assert.Throws<ArgumentException>(() => WhatsAppMessage.Inbound(ContactId, " ", WhatsAppMessageKind.Text, "Hola", null, Now));
        Assert.Throws<ArgumentException>(() => WhatsAppMessage.Inbound(
            ContactId, new string('w', WhatsAppMessage.MaxWaMessageIdLength + 1), WhatsAppMessageKind.Text, "Hola", null, Now));
    }

    [Fact]
    public void Long_bodies_are_truncated()
    {
        var message = WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.Text, new string('a', 10_000), null, Now);

        Assert.Equal(WhatsAppMessage.MaxBodyLength, message.Body!.Length);
    }

    /// <summary>
    /// Un emoji son dos unidades de UTF-16: si el corte cayera entre las dos, quedaría media letra, que Npgsql no puede
    /// mandar, y el webhook entero fallaría en cada reintento de Meta. El emoji queda afuera entero.
    /// </summary>
    [Fact]
    public void A_long_body_is_cut_without_splitting_an_emoji()
    {
        var body = new string('a', WhatsAppMessage.MaxBodyLength - 1) + "😀" + "b";

        var message = WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.Text, body, null, Now);

        Assert.Equal(new string('a', WhatsAppMessage.MaxBodyLength - 1), message.Body);
    }

    [Fact]
    public void An_emoji_that_fits_whole_is_kept()
    {
        var body = new string('a', WhatsAppMessage.MaxBodyLength - 2) + "😀" + "b";

        var message = WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.Text, body, null, Now);

        Assert.Equal(new string('a', WhatsAppMessage.MaxBodyLength - 2) + "😀", message.Body);
    }

    /// <summary>Postgres no guarda el carácter nulo en un texto: se saca, en lugar de perder el mensaje.</summary>
    [Theory]
    [InlineData("Ho\0la", "Hola")]
    [InlineData("\0", null)]
    [InlineData("Hola 😀", "Hola 😀")]
    public void Null_characters_are_not_stored(string body, string? expected)
    {
        var message = WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.Text, body, null, Now);

        Assert.Equal(expected, message.Body);
    }

    /// <summary>
    /// Media letra suelta no se puede mandar a Postgres: se cambia por el carácter de reemplazo (U+FFFD), en lugar de
    /// perder el mensaje.
    /// </summary>
    [Fact]
    public void A_loose_half_of_an_emoji_is_stored_as_the_replacement_character()
    {
        var endsWithHalf = WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.Text, "Hola " + HighSurrogate, null, Now);
        var startsWithHalf = WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.Text, LowSurrogate + "Hola", null, Now);

        Assert.Equal("Hola " + ReplacementCharacter, endsWithHalf.Body);
        Assert.Equal(ReplacementCharacter + "Hola", startsWithHalf.Body);
    }

    /// <summary>Un id con un carácter nulo o con media letra no se puede guardar ni usar de clave de un lock.</summary>
    [Fact]
    public void An_id_that_cannot_be_stored_is_not_valid()
    {
        foreach (var id in (string[])["wamid.\0", "wamid." + HighSurrogate])
        {
            Assert.False(WhatsAppMessage.IsValidWaMessageId(id));
            Assert.False(WhatsAppMessage.IsValidReplyId(id));
            Assert.Throws<ArgumentException>(() => WhatsAppMessage.Inbound(ContactId, id, WhatsAppMessageKind.Text, "Hola", null, Now));
            Assert.Throws<ArgumentException>(() => WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.ButtonReply, "Hola", id, Now));
        }
    }

    /// <summary>La Tarea 11 guarda los códigos mandados a números que nunca le escribieron al bot: no tienen contacto.</summary>
    [Fact]
    public void An_outbound_message_may_have_no_contact_and_has_no_status_until_meta_reports_one()
    {
        var message = WhatsAppMessage.Outbound(contactId: null, WaMessageId, WhatsAppMessageKind.Text, "[código]", Now);

        Assert.Null(message.ContactId);
        Assert.Equal(WhatsAppMessageDirection.Outbound, message.Direction);
        Assert.Equal("[código]", message.Body);
        Assert.Equal(Now, message.OccurredAtUtc);
        Assert.Null(message.Status);
        Assert.Null(message.StatusAtUtc);
    }

    [Fact]
    public void The_first_status_is_always_applied()
    {
        var message = Outbound();

        Assert.True(message.ApplyStatus(WhatsAppMessageStatus.Delivered, Now.AddSeconds(5), errorCode: null));

        Assert.Equal(WhatsAppMessageStatus.Delivered, message.Status);
        Assert.Equal(Now.AddSeconds(5), message.StatusAtUtc);
        Assert.Null(message.ErrorCode);
    }

    [Fact]
    public void A_newer_status_replaces_the_one_before()
    {
        var message = Outbound();
        message.ApplyStatus(WhatsAppMessageStatus.Sent, Now.AddSeconds(1), errorCode: null);

        Assert.True(message.ApplyStatus(WhatsAppMessageStatus.Read, Now.AddSeconds(9), errorCode: null));

        Assert.Equal(WhatsAppMessageStatus.Read, message.Status);
        Assert.Equal(Now.AddSeconds(9), message.StatusAtUtc);
    }

    /// <summary>Meta manda los eventos desordenados: un "delivered" viejo que llega después de un "read" no lo pisa.</summary>
    [Fact]
    public void An_older_status_is_ignored()
    {
        var message = Outbound();
        message.ApplyStatus(WhatsAppMessageStatus.Read, Now.AddSeconds(9), errorCode: null);

        Assert.False(message.ApplyStatus(WhatsAppMessageStatus.Delivered, Now.AddSeconds(5), errorCode: null));

        Assert.Equal(WhatsAppMessageStatus.Read, message.Status);
        Assert.Equal(Now.AddSeconds(9), message.StatusAtUtc);
    }

    /// <summary>Los timestamps de Meta van en segundos: dos estados del mismo segundo se ordenan por cómo avanza un mensaje.</summary>
    [Theory]
    [InlineData(WhatsAppMessageStatus.Sent, WhatsAppMessageStatus.Delivered, WhatsAppMessageStatus.Delivered)]
    [InlineData(WhatsAppMessageStatus.Delivered, WhatsAppMessageStatus.Read, WhatsAppMessageStatus.Read)]
    [InlineData(WhatsAppMessageStatus.Read, WhatsAppMessageStatus.Delivered, WhatsAppMessageStatus.Read)]
    [InlineData(WhatsAppMessageStatus.Read, WhatsAppMessageStatus.Sent, WhatsAppMessageStatus.Read)]
    [InlineData(WhatsAppMessageStatus.Sent, WhatsAppMessageStatus.Failed, WhatsAppMessageStatus.Failed)]
    [InlineData(WhatsAppMessageStatus.Delivered, WhatsAppMessageStatus.Delivered, WhatsAppMessageStatus.Delivered)]
    public void Statuses_of_the_same_second_follow_the_order_of_a_message(
        WhatsAppMessageStatus first,
        WhatsAppMessageStatus second,
        WhatsAppMessageStatus expected)
    {
        var message = Outbound();
        message.ApplyStatus(first, Now, errorCode: null);

        message.ApplyStatus(second, Now, errorCode: null);

        Assert.Equal(expected, message.Status);
    }

    [Fact]
    public void A_failure_keeps_the_error_code_and_is_final()
    {
        var message = Outbound();
        message.ApplyStatus(WhatsAppMessageStatus.Sent, Now, errorCode: null);

        Assert.True(message.ApplyStatus(WhatsAppMessageStatus.Failed, Now.AddSeconds(2), errorCode: 131026));
        Assert.False(message.ApplyStatus(WhatsAppMessageStatus.Read, Now.AddSeconds(30), errorCode: null));

        Assert.Equal(WhatsAppMessageStatus.Failed, message.Status);
        Assert.Equal(Now.AddSeconds(2), message.StatusAtUtc);
        Assert.Equal(131026, message.ErrorCode);
    }

    [Fact]
    public void Only_a_failure_keeps_an_error_code()
    {
        var message = Outbound();

        message.ApplyStatus(WhatsAppMessageStatus.Delivered, Now, errorCode: 131026);

        Assert.Null(message.ErrorCode);
    }

    [Fact]
    public void An_inbound_message_has_no_status()
    {
        var message = WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.Text, "Hola", null, Now);

        Assert.Throws<InvalidOperationException>(() => message.ApplyStatus(WhatsAppMessageStatus.Read, Now, errorCode: null));
    }

    [Fact]
    public void Processing_an_inbound_message_records_when()
    {
        var message = WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.Text, "Hola", null, Now);

        message.MarkProcessed(Now.AddSeconds(3));

        Assert.Equal(Now.AddSeconds(3), message.ProcessedAtUtc);
    }

    /// <summary>El bot lo procesa una sola vez: una segunda marca no cambia cuándo fue.</summary>
    [Fact]
    public void An_inbound_message_keeps_the_first_time_it_was_processed()
    {
        var message = WhatsAppMessage.Inbound(ContactId, WaMessageId, WhatsAppMessageKind.Text, "Hola", null, Now);
        message.MarkProcessed(Now.AddSeconds(3));

        message.MarkProcessed(Now.AddMinutes(5));

        Assert.Equal(Now.AddSeconds(3), message.ProcessedAtUtc);
    }

    /// <summary>Lo que procesa el bot es lo que manda la persona: un saliente no espera respuesta.</summary>
    [Fact]
    public void An_outbound_message_is_never_processed()
    {
        var message = Outbound();

        Assert.Throws<InvalidOperationException>(() => message.MarkProcessed(Now));
        Assert.Null(message.ProcessedAtUtc);
    }

    /// <summary>Lo que manda el bot, además de los textos: los mensajes con botones y las plantillas.</summary>
    [Theory]
    [InlineData(WhatsAppMessageKind.Interactive)]
    [InlineData(WhatsAppMessageKind.Template)]
    public void An_outbound_message_can_be_interactive_or_a_template(WhatsAppMessageKind kind)
    {
        var message = WhatsAppMessage.Outbound(ContactId, WaMessageId, kind, "[enlace de ingreso]", Now);

        Assert.Equal(kind, message.Kind);
    }

    private static WhatsAppMessage Outbound() =>
        WhatsAppMessage.Outbound(ContactId, WaMessageId, WhatsAppMessageKind.Text, "Listo.", Now.AddSeconds(-1));
}
