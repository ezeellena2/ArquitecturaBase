using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class WhatsAppContactRepository(ApplicationDbContext dbContext) : IWhatsAppContactRepository
{
    // Un prefijo por clase de clave: el mismo texto como BSUID y como número no comparte el lock.
    private const string UserIdentifierLockPrefix = "whatsapp-contact:user:";
    private const string WaIdLockPrefix = "whatsapp-contact:wa:";

    public Task LockAsync(
        IReadOnlyCollection<string> userIdentifiers,
        IReadOnlyCollection<string> waIds,
        CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync(
            userIdentifiers.Select(id => UserIdentifierLockPrefix + id).Concat(waIds.Select(id => WaIdLockPrefix + id)),
            cancellationToken);

    public Task<WhatsAppContact?> GetByUserIdentifierAsync(string userIdentifier, CancellationToken cancellationToken) =>
        dbContext.WhatsAppContacts.SingleOrDefaultAsync(contact => contact.UserIdentifier == userIdentifier, cancellationToken);

    // Entre los del mismo número, el que escribió último; el Id desempata dos del mismo segundo.
    public Task<WhatsAppContact?> GetLatestByWaIdAsync(string waId, CancellationToken cancellationToken) =>
        dbContext.WhatsAppContacts
            .Where(contact => contact.WaId == waId)
            .OrderByDescending(contact => contact.LastInboundAtUtc)
            .ThenByDescending(contact => contact.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(WhatsAppContact contact) => dbContext.WhatsAppContacts.Add(contact);
}
