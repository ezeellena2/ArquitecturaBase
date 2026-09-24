using ArquitecturaBase.Application.Interfaces.Persistence;
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

    /// <summary>
    /// <c>FOR NO KEY UPDATE SKIP LOCKED</c>: toma la fila sin esperar a nadie, y si otro la tiene, sigue sin ella. Es el
    /// lock de "voy a cambiar esta fila" (el bot la vincula a una cuenta), pero sin la parte que traba las claves
    /// foráneas: mientras el bot procesa un contacto, el webhook puede seguir guardándole mensajes y la cola de salida
    /// sus salientes. Sí espera, en cambio, el webhook que quiera actualizar el contacto (su nombre, su último mensaje).
    /// La transacción la confirma UnitOfWork, y con ella se suelta la fila.
    /// </summary>
    public async Task<WhatsAppContact?> GetForProcessingAsync(Guid contactId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        // Sin componer la consulta: el lock tiene que ir en la consulta que se manda tal cual.
        var locked = await dbContext.WhatsAppContacts
            .FromSql($"""SELECT * FROM "WhatsAppContacts" WHERE "Id" = {contactId} FOR NO KEY UPDATE SKIP LOCKED""")
            .ToListAsync(cancellationToken);

        return locked.SingleOrDefault();
    }

    /// <summary>
    /// <c>FOR NO KEY UPDATE</c>, el mismo lock que el bot pero sin <c>SKIP LOCKED</c>: espera a que el bot o un webhook
    /// suelten la fila. Postgres las toma en el orden en que las devuelve, y por eso van ordenadas por Id. Quedan en el
    /// contexto con lo que había después de esperar, y las lecturas que siguen devuelven estas mismas instancias.
    /// </summary>
    public async Task LockForNumberChangeAsync(Guid userId, string? waId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        // Sin componer la consulta, como en GetForProcessingAsync.
        var locking = waId is null
            ? dbContext.WhatsAppContacts.FromSql(
                $"""SELECT * FROM "WhatsAppContacts" WHERE "UserId" = {userId} ORDER BY "Id" FOR NO KEY UPDATE""")
            : dbContext.WhatsAppContacts.FromSql(
                $"""SELECT * FROM "WhatsAppContacts" WHERE "UserId" = {userId} OR "WaId" = {waId} ORDER BY "Id" FOR NO KEY UPDATE""");

        await locking.ToListAsync(cancellationToken);
    }

    public Task<WhatsAppContact?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.WhatsAppContacts.SingleOrDefaultAsync(contact => contact.UserId == userId, cancellationToken);

    /// <summary>
    /// <c>FOR NO KEY UPDATE NOWAIT</c>: el mismo lock que los demás, pero si otra transacción tiene la fila, Postgres
    /// corta con 55P03 en lugar de esperar. La transacción que ya la tiene (el cambio de número, que la tomó con
    /// <see cref="LockForNumberChangeAsync"/>) la vuelve a tomar sin problema.
    /// </summary>
    public async Task<WhatsAppContact?> GetByUserIdForUnlinkAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        // Sin componer la consulta, como en GetForProcessingAsync.
        var locked = await dbContext.WhatsAppContacts
            .FromSql($"""SELECT * FROM "WhatsAppContacts" WHERE "UserId" = {userId} FOR NO KEY UPDATE NOWAIT""")
            .ToListAsync(cancellationToken);

        return locked.SingleOrDefault();
    }

    public void Add(WhatsAppContact contact) => dbContext.WhatsAppContacts.Add(contact);
}
