using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.Features.WhatsApp.ReceiveWebhook;
using ArquitecturaBase.Application.UnitTests.TestDoubles.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Features.WhatsApp;

public sealed class ReceiveWhatsAppWebhookCommandHandlerTests
{
    private const string Bsuid = "AR.1102953142229032";
    private const string WaId = "5493413654813";

    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    private readonly LockLog _locks = new();
    private readonly InMemoryWhatsAppContactRepository _contacts;
    private readonly InMemoryWhatsAppMessageRepository _messages;

    public ReceiveWhatsAppWebhookCommandHandlerTests()
    {
        _contacts = new InMemoryWhatsAppContactRepository(_locks);
        _messages = new InMemoryWhatsAppMessageRepository(_locks);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_first_message_creates_the_contact_and_stays_pending()
    {
        var result = await HandleAsync(Batch(Text("wamid.1", From(WaId, Bsuid, "Ana Pérez"), "Hola", Now)));

        Assert.True(result.IsSuccess);
        var contact = Assert.Single(_contacts.Added);
        Assert.Equal(Bsuid, contact.UserIdentifier);
        Assert.Equal(WaId, contact.WaId);
        Assert.Equal("Ana Pérez", contact.ProfileName);
        Assert.Equal(Now, contact.LastInboundAtUtc);

        var message = Assert.Single(_messages.Added);
        Assert.Equal(contact.Id, message.ContactId);
        Assert.Equal(WhatsAppMessageDirection.Inbound, message.Direction);
        Assert.Equal("wamid.1", message.WaMessageId);
        Assert.Equal(WhatsAppMessageKind.Text, message.Kind);
        Assert.Equal("Hola", message.Body);
        Assert.Equal(Now, message.OccurredAtUtc);
        Assert.Null(message.ProcessedAtUtc);
    }

    [Fact]
    public async Task A_button_keeps_its_reply_id()
    {
        var button = new WhatsAppWebhookMessage("wamid.2", From(WaId, Bsuid, "Ana"), WhatsAppMessageKind.ButtonReply, "Crear cuenta", "CREATE_ACCOUNT", Now);

        await HandleAsync(Batch(button));

        var message = Assert.Single(_messages.Added);
        Assert.Equal(WhatsAppMessageKind.ButtonReply, message.Kind);
        Assert.Equal("Crear cuenta", message.Body);
        Assert.Equal("CREATE_ACCOUNT", message.ReplyId);
    }

    /// <summary>Meta reintenta un webhook que no recibió su 200: el mensaje que ya está guardado no se toca.</summary>
    [Fact]
    public async Task A_message_already_stored_is_a_retry_and_changes_nothing()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);
        _contacts.Contacts.Add(contact);
        _messages.Messages.Add(WhatsAppMessage.Inbound(contact.Id, "wamid.1", WhatsAppMessageKind.Text, "Hola", null, Now));

        var result = await HandleAsync(Batch(Text("wamid.1", From(WaId, Bsuid, "Ana Pérez"), "Hola", Now.AddMinutes(5))));

        Assert.True(result.IsSuccess);
        Assert.Empty(_messages.Added);
        Assert.Empty(_contacts.Added);
        Assert.Equal("Ana", contact.ProfileName);
        Assert.Equal(Now, contact.LastInboundAtUtc);
    }

    [Fact]
    public async Task The_same_message_twice_in_one_webhook_is_saved_once()
    {
        var hola = Text("wamid.1", From(WaId, Bsuid, "Ana"), "Hola", Now);

        await HandleAsync(Batch(hola, hola));

        Assert.Single(_messages.Added);
        Assert.Single(_contacts.Added);
    }

    [Fact]
    public async Task Two_messages_of_a_new_person_in_one_webhook_create_a_single_contact()
    {
        await HandleAsync(Batch(
            Text("wamid.1", From(WaId, Bsuid, "Ana"), "Hola", Now),
            Text("wamid.2", From(WaId, Bsuid, "Ana"), "¿Hay alguien?", Now.AddSeconds(3))));

        var contact = Assert.Single(_contacts.Added);
        Assert.Equal(2, _messages.Added.Count);
        Assert.All(_messages.Added, message => Assert.Equal(contact.Id, message.ContactId));
        Assert.Equal(Now.AddSeconds(3), contact.LastInboundAtUtc);
    }

    [Fact]
    public async Task A_returning_person_is_found_by_the_bsuid_and_updated()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);
        _contacts.Contacts.Add(contact);

        await HandleAsync(Batch(Text("wamid.2", From("5493415550000", Bsuid, "Ana Pérez"), "Hola de nuevo", Now.AddDays(1))));

        Assert.Empty(_contacts.Added);
        Assert.Equal("5493415550000", contact.WaId);
        Assert.Equal("Ana Pérez", contact.ProfileName);
        Assert.Equal(Now.AddDays(1), contact.LastInboundAtUtc);
        Assert.Equal(contact.Id, Assert.Single(_messages.Added).ContactId);
    }

    [Fact]
    public async Task A_person_known_only_by_the_number_is_found_by_it_and_adopts_the_bsuid()
    {
        var contact = WhatsAppContact.Create(WaId, userIdentifier: null, "Ana", Now);
        _contacts.Contacts.Add(contact);

        await HandleAsync(Batch(Text("wamid.2", From(WaId, Bsuid, "Ana"), "Hola", Now.AddMinutes(1))));

        Assert.Empty(_contacts.Added);
        Assert.Equal(Bsuid, contact.UserIdentifier);
    }

    [Fact]
    public async Task A_message_without_bsuid_finds_the_contact_by_the_number()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);
        _contacts.Contacts.Add(contact);

        await HandleAsync(Batch(Text("wamid.2", From(WaId, userIdentifier: null, "Ana"), "Hola", Now.AddMinutes(1))));

        Assert.Empty(_contacts.Added);
        Assert.Equal(contact.Id, Assert.Single(_messages.Added).ContactId);
    }

    /// <summary>Otro BSUID con el mismo número es otra persona, o la misma con otro número: es otro contacto (spec 6.5).</summary>
    [Fact]
    public async Task Another_bsuid_with_the_same_number_is_another_contact()
    {
        var previous = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);
        _contacts.Contacts.Add(previous);

        await HandleAsync(Batch(Text("wamid.2", From(WaId, "AR.999", "Beto"), "Hola", Now.AddMinutes(1))));

        var contact = Assert.Single(_contacts.Added);
        Assert.Equal("AR.999", contact.UserIdentifier);
        Assert.Equal("Beto", contact.ProfileName);
        Assert.Equal(Bsuid, previous.UserIdentifier);
        Assert.Equal("Ana", previous.ProfileName);
    }

    [Fact]
    public async Task A_status_updates_the_outbound_message_only_when_it_is_newer()
    {
        var outbound = Outbound("wamid.out");

        await HandleAsync(Batch(
            Status("wamid.out", WhatsAppMessageStatus.Read, Now.AddSeconds(9)),
            Status("wamid.out", WhatsAppMessageStatus.Delivered, Now.AddSeconds(5))));

        Assert.Equal(WhatsAppMessageStatus.Read, outbound.Status);
        Assert.Equal(Now.AddSeconds(9), outbound.StatusAtUtc);
    }

    [Fact]
    public async Task A_failure_keeps_the_error_code()
    {
        var outbound = Outbound("wamid.out");

        await HandleAsync(Batch(Status("wamid.out", WhatsAppMessageStatus.Failed, Now, errorCode: 131026)));

        Assert.Equal(WhatsAppMessageStatus.Failed, outbound.Status);
        Assert.Equal(131026, outbound.ErrorCode);
    }

    [Fact]
    public async Task A_status_of_a_message_that_is_not_stored_is_ignored()
    {
        var result = await HandleAsync(Batch(Status("wamid.unknown", WhatsAppMessageStatus.Delivered, Now)));

        Assert.True(result.IsSuccess);
        Assert.Empty(_messages.Added);
    }

    /// <summary>Un aviso de estado con el id de un mensaje entrante no tiene sentido: se ignora sin romper el resto.</summary>
    [Fact]
    public async Task A_status_with_the_id_of_an_inbound_message_is_ignored()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);
        var inbound = WhatsAppMessage.Inbound(contact.Id, "wamid.in", WhatsAppMessageKind.Text, "Hola", null, Now);
        _messages.Messages.Add(inbound);

        var result = await HandleAsync(Batch(Status("wamid.in", WhatsAppMessageStatus.Read, Now)));

        Assert.True(result.IsSuccess);
        Assert.Null(inbound.Status);
    }

    /// <summary>
    /// Los locks se toman antes de mirar nada: por cada persona, su BSUID y su número, y por cada aviso de estado, su
    /// mensaje. Primero los de los contactos y después los de los mensajes, siempre en ese orden.
    /// </summary>
    [Fact]
    public async Task Locks_every_contact_and_every_status_before_looking_at_anything()
    {
        await HandleAsync(new WhatsAppWebhookBatch(
            [
                Text("wamid.1", From(WaId, Bsuid, "Ana"), "Hola", Now),
                Text("wamid.2", From("5493415550000", userIdentifier: null, "Beto"), "Hola", Now),
                Text("wamid.3", From(waId: null, "AR.777", "Caro"), "Hola", Now),
            ],
            [Status("wamid.out", WhatsAppMessageStatus.Sent, Now)]));

        // El orden entre las claves de cada grupo lo pone el repositorio, que las ordena antes de tomarlas (lo prueba
        // WhatsAppLockOrderTests, contra Postgres).
        Assert.Equal(
            ["message:wamid.out", "user:AR.1102953142229032", "user:AR.777", "wa:5493413654813", "wa:5493415550000"],
            _locks.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("message:wamid.out", _locks.Keys[^1]);

        // Y todos antes de la primera lectura: si el segundo de dos webhooks simultáneos mirara antes de esperar su
        // turno, no vería lo que guardó el primero y chocaría con el índice único.
        var firstRead = _locks.Events.FindIndex(LockLog.IsRead);
        var lastLock = _locks.Events.FindLastIndex(entry => !LockLog.IsRead(entry));
        Assert.True(firstRead >= 0, "The handler did not read anything.");
        Assert.True(lastLock < firstRead, "The handler read before taking every lock: " + string.Join(", ", _locks.Events));
    }

    [Fact]
    public async Task An_empty_webhook_takes_no_locks()
    {
        var result = await HandleAsync(WhatsAppWebhookBatch.Empty);

        Assert.True(result.IsSuccess);
        Assert.Empty(_locks.Keys);
    }

    [Fact]
    public void The_command_never_prints_what_the_webhook_brought()
    {
        var command = new ReceiveWhatsAppWebhookCommand(Batch(Text("wamid.1", From(WaId, Bsuid, "Ana"), "mi secreto", Now)));

        var printed = command.ToString();

        Assert.DoesNotContain("mi secreto", printed, StringComparison.Ordinal);
        Assert.DoesNotContain(Bsuid, printed, StringComparison.Ordinal);
        Assert.DoesNotContain(WaId, printed, StringComparison.Ordinal);
    }

    private Task<Result> HandleAsync(WhatsAppWebhookBatch batch) =>
        new ReceiveWhatsAppWebhookCommandHandler(_contacts, _messages, NullLogger<ReceiveWhatsAppWebhookCommandHandler>.Instance)
            .Handle(new ReceiveWhatsAppWebhookCommand(batch), Ct);

    private WhatsAppMessage Outbound(string waMessageId)
    {
        var message = WhatsAppMessage.Outbound(contactId: null, waMessageId, WhatsAppMessageKind.Text, "Listo.", Now.AddSeconds(-1));
        _messages.Messages.Add(message);

        return message;
    }

    private static WhatsAppWebhookBatch Batch(params WhatsAppWebhookMessage[] messages) => new(messages, []);

    private static WhatsAppWebhookBatch Batch(params WhatsAppWebhookStatus[] statuses) => new([], statuses);

    private static WhatsAppWebhookContact From(string? waId, string? userIdentifier, string? profileName) =>
        new(waId, userIdentifier, profileName);

    private static WhatsAppWebhookMessage Text(string waMessageId, WhatsAppWebhookContact from, string body, DateTime occurredAtUtc) =>
        new(waMessageId, from, WhatsAppMessageKind.Text, body, ReplyId: null, occurredAtUtc);

    private static WhatsAppWebhookStatus Status(string waMessageId, WhatsAppMessageStatus status, DateTime occurredAtUtc, int? errorCode = null) =>
        new(waMessageId, status, occurredAtUtc, errorCode);
}
