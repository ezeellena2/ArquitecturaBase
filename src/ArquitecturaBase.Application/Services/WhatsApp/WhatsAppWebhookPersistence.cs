using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.WhatsApp;

/// <summary>
/// Guarda los contactos, los mensajes entrantes y los estados de los salientes de un webhook (sección 7 del spec del
/// ingreso con WhatsApp). Todo es idempotente, porque Meta reintenta durante 7 días y manda los eventos desordenados:
/// un mensaje que ya está guardado es un reintento y no se toca, un estado solo pisa a uno más viejo y un estado de un
/// mensaje que no se guardó se ignora. Siempre termina bien: nada de lo que trae un webhook firmado es un error de
/// quien lo manda, y un error haría que Meta lo reintente durante días.
/// </summary>
internal sealed partial class WhatsAppWebhookPersistence(
    IWhatsAppContactRepository contacts,
    IWhatsAppMessageRepository messages,
    IUnitOfWork unitOfWork,
    ILogger<WhatsAppWebhookPersistence> logger)
    : IWhatsAppWebhookPersistence
{
    public async Task PersistAsync(WhatsAppWebhookBatch batch, CancellationToken cancellationToken)
    {
        if (batch.IsEmpty)
        {
            return;
        }

        // Antes de mirar nada: dos webhooks simultáneos de la misma persona (un reintento de Meta que se cruza con el
        // original) esperan acá, y el segundo ve lo que guardó el primero. Primero los contactos y después los
        // mensajes, como todo el que tome los dos.
        await contacts.LockAsync(
            Distinct(batch.Messages.Select(message => message.From.UserIdentifier)),
            Distinct(batch.Messages.Select(message => message.From.WaId)),
            cancellationToken);
        await messages.LockAsync(Distinct(batch.Statuses.Select(status => status.WaMessageId)), cancellationToken);

        var (saved, repeated) = await SaveInboundAsync(batch.Messages, cancellationToken);
        var (applied, ignored) = await ApplyStatusesAsync(batch.Statuses, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogReceived(logger, saved, repeated, applied, ignored);
    }

    private async Task<(int Saved, int Repeated)> SaveInboundAsync(
        IReadOnlyList<WhatsAppWebhookMessage> inbound,
        CancellationToken cancellationToken)
    {
        if (inbound.Count == 0)
        {
            return (0, 0);
        }

        var stored = await messages.ListExistingIdsAsync(
            Distinct(inbound.Select(message => message.WaMessageId)), cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new ResolvedContacts();
        var saved = 0;

        // Del más viejo al más nuevo: así el contacto queda con el nombre y el número del último mensaje.
        foreach (var message in inbound.OrderBy(message => message.OccurredAtUtc))
        {
            if (stored.Contains(message.WaMessageId) || !seen.Add(message.WaMessageId))
            {
                continue;
            }

            var contact = await ResolveContactAsync(message.From, message.OccurredAtUtc, resolved, cancellationToken);

            messages.Add(WhatsAppMessage.Inbound(
                contact.Id, message.WaMessageId, message.Kind, message.Body, message.ReplyId, message.OccurredAtUtc));
            saved++;
        }

        return (saved, inbound.Count - saved);
    }

    /// <summary>
    /// Primero por el BSUID y después por el número (sección 6.5 del spec). Un contacto encontrado por el número sirve
    /// si alguno de los dos no tiene BSUID; si los dos tienen y son distintos, es otra persona, o la misma con otro
    /// número, y va a otro contacto.
    /// </summary>
    private async Task<WhatsAppContact> ResolveContactAsync(
        WhatsAppWebhookContact from,
        DateTime occurredAtUtc,
        ResolvedContacts resolved,
        CancellationToken cancellationToken)
    {
        WhatsAppContact? contact = null;

        if (from.UserIdentifier is { } userIdentifier)
        {
            contact = resolved.ByUserIdentifier(userIdentifier)
                ?? await contacts.GetByUserIdentifierAsync(userIdentifier, cancellationToken);
        }

        if (contact is null && from.WaId is { } waId)
        {
            var byNumber = resolved.ByWaId(waId) ?? await contacts.GetLatestByWaIdAsync(waId, cancellationToken);

            if (byNumber is not null && (from.UserIdentifier is null || byNumber.UserIdentifier is null))
            {
                contact = byNumber;
            }
        }

        if (contact is null)
        {
            contact = WhatsAppContact.Create(from.WaId, from.UserIdentifier, from.ProfileName, occurredAtUtc);
            contacts.Add(contact);
        }
        else
        {
            contact.RecordInbound(from.WaId, from.UserIdentifier, from.ProfileName, occurredAtUtc);
        }

        // Uno recién creado no aparece en las consultas hasta que se guarda: el próximo mensaje del mismo lote lo
        // encuentra acá.
        resolved.Remember(contact);

        return contact;
    }

    private async Task<(int Applied, int Ignored)> ApplyStatusesAsync(
        IReadOnlyList<WhatsAppWebhookStatus> statuses,
        CancellationToken cancellationToken)
    {
        if (statuses.Count == 0)
        {
            return (0, 0);
        }

        var outbound = (await messages.ListOutboundAsync(
                Distinct(statuses.Select(status => status.WaMessageId)), cancellationToken))
            .ToDictionary(message => message.WaMessageId, StringComparer.Ordinal);
        var applied = 0;

        // El orden no importa: cada mensaje se queda con el estado más nuevo, lleguen como lleguen.
        foreach (var status in statuses)
        {
            if (outbound.TryGetValue(status.WaMessageId, out var message)
                && message.ApplyStatus(status.Status, status.OccurredAtUtc, status.ErrorCode))
            {
                applied++;
            }
        }

        return (applied, statuses.Count - applied);
    }

    private static string[] Distinct(IEnumerable<string?> values) =>
        [.. values.OfType<string>().Distinct(StringComparer.Ordinal)];

    // Solo cuántos: ni textos, ni BSUID, ni números.
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Received a WhatsApp webhook: {SavedMessages} new messages, {RepeatedMessages} repeated, {AppliedStatuses} statuses applied and {IgnoredStatuses} ignored")]
    private static partial void LogReceived(
        ILogger logger, int savedMessages, int repeatedMessages, int appliedStatuses, int ignoredStatuses);

    /// <summary>Los contactos que ya se resolvieron en este lote, por su BSUID y por su número.</summary>
    private sealed class ResolvedContacts
    {
        private readonly Dictionary<string, WhatsAppContact> _byUserIdentifier = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WhatsAppContact> _byWaId = new(StringComparer.Ordinal);

        public WhatsAppContact? ByUserIdentifier(string userIdentifier) => _byUserIdentifier.GetValueOrDefault(userIdentifier);

        public WhatsAppContact? ByWaId(string waId) => _byWaId.GetValueOrDefault(waId);

        public void Remember(WhatsAppContact contact)
        {
            if (contact.UserIdentifier is { } userIdentifier)
            {
                _byUserIdentifier[userIdentifier] = contact;
            }

            if (contact.WaId is { } waId)
            {
                _byWaId[waId] = contact;
            }
        }
    }
}
