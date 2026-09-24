using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.WhatsApp;

/// <summary>
/// Lo que hicieron los dos repositorios, en orden: cada clave de lock que se tomó y cada lectura. Así un test puede
/// ver no solo qué se bloqueó, sino si se bloqueó antes de mirar.
/// </summary>
internal sealed class LockLog
{
    private const string ReadPrefix = "read:";

    /// <summary>Todo, en orden: las claves de los locks y las lecturas ("read:" y el método).</summary>
    public List<string> Events { get; } = [];

    /// <summary>Solo las claves de los locks, en el orden en que se tomaron.</summary>
    public List<string> Keys => [.. Events.Where(entry => !IsRead(entry))];

    public static bool IsRead(string entry) => entry.StartsWith(ReadPrefix, StringComparison.Ordinal);

    public void Lock(IEnumerable<string> keys) => Events.AddRange(keys);

    public void Read(string method) => Events.Add(ReadPrefix + method);
}

internal sealed class InMemoryWhatsAppContactRepository(LockLog locks) : IWhatsAppContactRepository
{
    /// <summary>Los que ya estaban guardados.</summary>
    public List<WhatsAppContact> Contacts { get; } = [];

    /// <summary>Los que agregó el caso de uso. Como en EF, no aparecen en las consultas hasta que se guardan.</summary>
    public List<WhatsAppContact> Added { get; } = [];

    public Task LockAsync(IReadOnlyCollection<string> userIdentifiers, IReadOnlyCollection<string> waIds, CancellationToken cancellationToken)
    {
        locks.Lock(userIdentifiers.Select(id => "user:" + id));
        locks.Lock(waIds.Select(id => "wa:" + id));

        return Task.CompletedTask;
    }

    public Task<WhatsAppContact?> GetByUserIdentifierAsync(string userIdentifier, CancellationToken cancellationToken)
    {
        locks.Read(nameof(GetByUserIdentifierAsync));

        return Task.FromResult(Contacts.SingleOrDefault(contact => contact.UserIdentifier == userIdentifier));
    }

    public Task<WhatsAppContact?> GetLatestByWaIdAsync(string waId, CancellationToken cancellationToken)
    {
        locks.Read(nameof(GetLatestByWaIdAsync));

        return Task.FromResult(Contacts.Where(contact => contact.WaId == waId).MaxBy(contact => contact.LastInboundAtUtc));
    }

    /// <summary>Los contactos que está procesando otra instancia: <see cref="GetForProcessingAsync"/> no los devuelve.</summary>
    public HashSet<Guid> LockedElsewhere { get; } = [];

    public Task<WhatsAppContact?> GetForProcessingAsync(Guid contactId, CancellationToken cancellationToken)
    {
        locks.Lock(["processing:" + contactId]);

        return Task.FromResult(LockedElsewhere.Contains(contactId)
            ? null
            : Contacts.SingleOrDefault(contact => contact.Id == contactId));
    }

    public Task LockForNumberChangeAsync(Guid userId, string? waId, CancellationToken cancellationToken)
    {
        locks.Lock(waId is null ? ["number-change:" + userId] : ["number-change:" + userId, "number-change:wa:" + waId]);

        return Task.CompletedTask;
    }

    public Task<WhatsAppContact?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        locks.Read(nameof(GetByUserIdAsync));

        return Task.FromResult(Contacts.SingleOrDefault(contact => contact.UserId == userId));
    }

    public Task<WhatsAppContact?> GetByUserIdForUnlinkAsync(Guid userId, CancellationToken cancellationToken)
    {
        locks.Lock(["unlink:" + userId]);

        return Task.FromResult(Contacts.SingleOrDefault(contact => contact.UserId == userId));
    }

    public void Add(WhatsAppContact contact) => Added.Add(contact);
}

internal sealed class InMemoryWhatsAppMessageRepository(LockLog locks) : IWhatsAppMessageRepository
{
    /// <summary>Los que ya estaban guardados.</summary>
    public List<WhatsAppMessage> Messages { get; } = [];

    /// <summary>Los que agregó el caso de uso.</summary>
    public List<WhatsAppMessage> Added { get; } = [];

    public Task LockAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken)
    {
        locks.Lock(waMessageIds.Select(id => "message:" + id));

        return Task.CompletedTask;
    }

    public Task<IReadOnlySet<string>> ListExistingIdsAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken)
    {
        locks.Read(nameof(ListExistingIdsAsync));

        return Task.FromResult<IReadOnlySet<string>>(Messages
            .Select(message => message.WaMessageId)
            .Where(waMessageIds.Contains)
            .ToHashSet(StringComparer.Ordinal));
    }

    public Task<IReadOnlyList<WhatsAppMessage>> ListOutboundAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken)
    {
        locks.Read(nameof(ListOutboundAsync));

        return Task.FromResult<IReadOnlyList<WhatsAppMessage>>(Messages
            .Where(message => message.Direction == WhatsAppMessageDirection.Outbound && waMessageIds.Contains(message.WaMessageId))
            .ToList());
    }

    public Task<IReadOnlyList<Guid>> ListContactsWithPendingInboundAsync(
        IReadOnlyCollection<Guid> excluded,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Pending()
            .GroupBy(message => message.ContactId!.Value)
            .Where(group => !excluded.Contains(group.Key))
            .OrderBy(group => group.Min(message => message.OccurredAtUtc))
            .Take(limit)
            .Select(group => group.Key)
            .ToList());

    public Task<IReadOnlyList<WhatsAppMessage>> ListPendingInboundAsync(Guid contactId, CancellationToken cancellationToken)
    {
        locks.Read(nameof(ListPendingInboundAsync));

        return Task.FromResult<IReadOnlyList<WhatsAppMessage>>(Pending()
            .Where(message => message.ContactId == contactId)
            .OrderBy(message => message.OccurredAtUtc)
            .ToList());
    }

    public void Add(WhatsAppMessage message) => Added.Add(message);

    private IEnumerable<WhatsAppMessage> Pending() =>
        Messages.Where(message => message.Direction == WhatsAppMessageDirection.Inbound && message.ProcessedAtUtc is null);
}

internal sealed record FakeAppName(string Value) : IAppName;
