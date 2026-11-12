using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;

/// <summary>
/// Vacía el contenido de los mensajes vencidos; preserva las respuestas de Flow pendientes. Queda fuera de IUnitOfWork.ExecuteInTransactionAsync a propósito: es una
/// sola sentencia, idempotente, en autocommit, sobre una entidad que no es IAuditable ni ISoftDeletable. Es la única
/// clase que puede usar ExecuteUpdate (lo verifica TransactionBoundaryTests). WhatsAppMessageRetentionService la corre
/// aunque WhatsApp estÃ© apagado.
/// </summary>
internal sealed class WhatsAppMessageRetentionRepository(ApplicationDbContext dbContext)
    : IWhatsAppMessageRetentionRepository
{
    public async Task<int> ClearExpiredBodiesAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        // ExecuteUpdate no pasa por los interceptores. WhatsAppMessage no es IAuditable ni ISoftDeletable.
        // Sin índice por fecha: una corrida diaria recorre la tabla entera a esta escala. Si crece, un índice parcial
        // sobre OccurredAtUtc y contenido presente deja afuera los mensajes que ya se vaciaron.
        var messages = await dbContext.Set<WhatsAppMessage>()
            .Where(message => message.OccurredAtUtc < cutoffUtc
                && (message.Body != null || message.QuickReplyTitlesJson != null || message.AssistantReplyJson != null || message.Kind == WhatsAppMessageKind.Audio && (message.AudioMediaId != null || message.AudioStorageKey != null || message.AudioContextJson != null)
                    || message.Kind == WhatsAppMessageKind.Spreadsheet && (message.SpreadsheetMediaId != null || message.SpreadsheetFileName != null || message.SpreadsheetStorageKey != null || message.SpreadsheetPreparedJson != null)
                    || message.Kind == WhatsAppMessageKind.ReceivedDocument && (message.DocumentMediaId != null || message.DocumentFileName != null || message.DocumentStorageKey != null || message.DocumentReadingJson != null || message.DocumentContextJson != null)
                    || message.Latitude != null
                    || message.LocationAddress != null || message.ContactName != null || message.ContactPhonesJson != null
                    || (message.ProtectedFlowResponse != null && message.ProcessedAtUtc != null)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Body, (string?)null)
                // W1-A: the point of a received location goes with the body.
                .SetProperty(message => message.Latitude, (decimal?)null)
                .SetProperty(message => message.Longitude, (decimal?)null)
                // W3: and so do the address of a shared place and the name and the phones of a shared contact.
                .SetProperty(message => message.LocationAddress, (string?)null)
                .SetProperty(message => message.ContactName, (string?)null)
                .SetProperty(message => message.ContactPhonesJson, (string?)null)
                .SetProperty(message => message.QuickReplyTitlesJson, (string?)null)
                .SetProperty(message => message.AssistantReplyJson, (string?)null)
                .SetProperty(message => message.AudioMediaId, (string?)null)
                .SetProperty(message => message.AudioContentType, (string?)null)
                .SetProperty(message => message.AudioSha256, (string?)null)
                .SetProperty(message => message.AudioStorageKey, (string?)null)
                .SetProperty(message => message.AudioSize, (long?)null)
                .SetProperty(message => message.AudioContextJson, (string?)null)
                .SetProperty(message => message.AudioState, message => message.Kind == WhatsAppMessageKind.Audio ? WhatsAppAudioState.Expired : message.AudioState)
                // A spreadsheet's descriptor, its copy and what was prepared go with the body; its state and its reason stay.
                .SetProperty(message => message.SpreadsheetMediaId, (string?)null)
                .SetProperty(message => message.SpreadsheetContentType, (string?)null)
                .SetProperty(message => message.SpreadsheetSha256, (string?)null)
                .SetProperty(message => message.SpreadsheetFileName, (string?)null)
                .SetProperty(message => message.SpreadsheetStorageKey, (string?)null)
                .SetProperty(message => message.SpreadsheetSize, (long?)null)
                .SetProperty(message => message.SpreadsheetCopySha256, (string?)null)
                .SetProperty(message => message.SpreadsheetPreparedJson, (string?)null)
                // A received document's descriptor, its copy, its reading and the context of its request go with the body too; its
                // reason stays, and so does its state, except a reading in course, which ends expired.
                .SetProperty(message => message.DocumentState, message => message.DocumentState == WhatsAppDocumentState.Reading
                    ? WhatsAppDocumentState.Expired : message.DocumentState)
                .SetProperty(message => message.DocumentMediaId, (string?)null)
                .SetProperty(message => message.DocumentContentType, (string?)null)
                .SetProperty(message => message.DocumentSha256, (string?)null)
                .SetProperty(message => message.DocumentFileName, (string?)null)
                .SetProperty(message => message.DocumentStorageKey, (string?)null)
                .SetProperty(message => message.DocumentSize, (long?)null)
                .SetProperty(message => message.DocumentCopySha256, (string?)null)
                .SetProperty(message => message.DocumentPages, (int?)null)
                .SetProperty(message => message.DocumentReadingJson, (string?)null)
                .SetProperty(message => message.DocumentContextJson, (string?)null)
                .SetProperty(message => message.ProtectedFlowResponse, (byte[]?)null), cancellationToken);
        var outgoing = await dbContext.Set<WhatsAppOutgoingMessage>()
            .Where(message => (message.Body != null || message.AssistantReplyJson != null) && message.RequestedAtUtc < cutoffUtc
                && message.State != WhatsAppOutgoingMessageState.Pending && message.State != WhatsAppOutgoingMessageState.Sending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.Body, (string?)null)
                .SetProperty(message => message.AssistantReplyJson, (string?)null), cancellationToken);
        return messages + outgoing;
    }
    public async Task<int> ClearExpiredAssistantContentAsync(DateTime nowUtc,int limit,CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        var sessions = await dbContext.Set<WhatsAppFlowSession>()
            .Where(session=>session.AssistantContentExpiresAtUtc<=nowUtc && session.ProtectedAssistantContent.Length>0)
            .OrderBy(session=>session.AssistantContentExpiresAtUtc).ThenBy(session=>session.Id).Take(limit).ToListAsync(cancellationToken);
        foreach(var session in sessions) session.ClearAssistantContent(nowUtc);
        // Casos del asistente: what ended without a message keeps its case while the content of its reference lives.
        var outcomes = await dbContext.Set<WhatsAppAssistantRequest>()
            .Where(request=>request.OutcomeJson!=null && request.ContentExpiresAtUtc<=nowUtc)
            .OrderBy(request=>request.ContentExpiresAtUtc).ThenBy(request=>request.Id).Take(limit).ToListAsync(cancellationToken);
        foreach(var request in outcomes) request.ClearOutcome();
        return sessions.Count + outcomes.Count;
    }

}
