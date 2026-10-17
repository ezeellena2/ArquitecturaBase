using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;

/// <summary>
/// VacÃ­a el contenido de los mensajes vencidos; preserva las respuestas de Flow pendientes. Queda fuera de IUnitOfWork.ExecuteInTransactionAsync a propÃ³sito: es una
/// sola sentencia, idempotente, en autocommit, sobre una entidad que no es IAuditable ni ISoftDeletable. Es la Ãºnica
/// clase que puede usar ExecuteUpdate (lo verifica TransactionBoundaryTests). WhatsAppMessageRetentionService la corre
/// aunque WhatsApp estÃ© apagado.
/// </summary>
internal sealed class WhatsAppMessageRetentionRepository(ApplicationDbContext dbContext)
    : IWhatsAppMessageRetentionRepository
{
    public async Task<int> ClearExpiredBodiesAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        // ExecuteUpdate no pasa por los interceptores. WhatsAppMessage no es IAuditable ni ISoftDeletable.
        // Sin Ã­ndice por fecha: una corrida diaria recorre la tabla entera a esta escala. Si crece, un Ã­ndice parcial
        // sobre OccurredAtUtc y contenido presente deja afuera los mensajes que ya se vaciaron.
        var messages = await dbContext.Set<WhatsAppMessage>()
            .Where(message => message.OccurredAtUtc < cutoffUtc
                && (message.Body != null || message.Kind == WhatsAppMessageKind.Audio && (message.AudioMediaId != null || message.AudioStorageKey != null || message.AudioContextJson != null) || (message.ProtectedFlowResponse != null && message.ProcessedAtUtc != null)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Body, (string?)null)
                .SetProperty(message => message.AudioMediaId, (string?)null)
                .SetProperty(message => message.AudioContentType, (string?)null)
                .SetProperty(message => message.AudioSha256, (string?)null)
                .SetProperty(message => message.AudioStorageKey, (string?)null)
                .SetProperty(message => message.AudioSize, (long?)null)
                .SetProperty(message => message.AudioContextJson, (string?)null)
                .SetProperty(message => message.AudioState, message => message.Kind == WhatsAppMessageKind.Audio ? WhatsAppAudioState.Expired : message.AudioState)
                .SetProperty(message => message.ProtectedFlowResponse, (byte[]?)null), cancellationToken);
        var outgoing = await dbContext.Set<WhatsAppOutgoingMessage>()
            .Where(message => message.Body != null && message.RequestedAtUtc < cutoffUtc
                && message.State != WhatsAppOutgoingMessageState.Pending && message.State != WhatsAppOutgoingMessageState.Sending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.Body, (string?)null), cancellationToken);
        return messages + outgoing;
    }
}
