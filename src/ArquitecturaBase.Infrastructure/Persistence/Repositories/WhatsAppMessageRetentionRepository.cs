using ArquitecturaBase.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

/// <summary>
/// Vacía el texto de los mensajes vencidos. Queda fuera de IUnitOfWork.ExecuteInTransactionAsync a propósito: es una
/// sola sentencia, idempotente, en autocommit, sobre una entidad que no es IAuditable ni ISoftDeletable. Es la única
/// clase que puede usar ExecuteUpdate (lo verifica TransactionBoundaryTests). WhatsAppMessageRetentionService la corre
/// aunque WhatsApp esté apagado.
/// </summary>
internal sealed class WhatsAppMessageRetentionRepository(ApplicationDbContext dbContext)
    : IWhatsAppMessageRetentionRepository
{
    public Task<int> ClearExpiredBodiesAsync(DateTime cutoffUtc, CancellationToken cancellationToken) =>
        // ExecuteUpdate no pasa por los interceptores. WhatsAppMessage no es IAuditable ni ISoftDeletable.
        // Sin índice por fecha: una corrida diaria recorre la tabla entera a esta escala. Si crece, un índice parcial
        // sobre OccurredAtUtc con "Body" IS NOT NULL deja afuera los mensajes que ya se vaciaron.
        dbContext.WhatsAppMessages
            .Where(message => message.Body != null && message.OccurredAtUtc < cutoffUtc)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.Body, (string?)null), cancellationToken);
}
