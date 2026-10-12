using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
internal sealed class WhatsAppFlowSessionReader(ApplicationDbContext dbContext) : IWhatsAppFlowSessionReader
{
    public async Task<IReadOnlyList<WhatsAppPendingFlowReplyRow>> ListPendingFlowRepliesAsync(int limit, CancellationToken cancellationToken) =>
        await dbContext.Set<WhatsAppMessage>().AsNoTracking()
            .Where(message => message.Direction == WhatsAppMessageDirection.Inbound
                && message.Kind == WhatsAppMessageKind.FlowReply && message.ProcessedAtUtc == null
                && message.ContactId != null && message.ProtectedFlowResponse != null)
            .OrderBy(message => message.OccurredAtUtc).ThenBy(message => message.Id).Take(limit)
            .Select(message => new WhatsAppPendingFlowReplyRow(message.WaMessageId, message.ContactId!.Value,
                message.OccurredAtUtc, message.ProtectedFlowResponse!))
            .ToListAsync(cancellationToken);

    public Task<WhatsAppFormBindingRow?> FindBindingAsync(Guid definitionId, Guid versionId, string culture, CancellationToken cancellationToken) =>
        (from binding in dbContext.Set<WhatsAppFlowBinding>().AsNoTracking()
         join operation in dbContext.Set<WhatsAppFlowOperation>() on binding.OperationId equals operation.Id
         where operation.DefinitionId == definitionId && operation.VersionId == versionId && binding.VersionId == versionId
             && binding.Culture == culture && binding.State == WhatsAppFlowPublicationState.Published && binding.MetaFlowId != null
         select new WhatsAppFormBindingRow(binding.Id, operation.Id, operation.NeutralOperationId, operation.DefinitionId,
             binding.VersionId, binding.Culture, binding.TechnicalName, binding.MetaFlowId!, binding.ProtectedProjection, operation.ProtectedManifest))
        .SingleOrDefaultAsync(cancellationToken);
    public Task<WhatsAppFormDispatchRow?> FindDispatchAsync(Guid outgoingId, CancellationToken cancellationToken) =>
        (from outgoing in dbContext.Set<WhatsAppOutgoingMessage>().AsNoTracking()
         join session in dbContext.Set<WhatsAppFlowSession>() on outgoing.FlowSessionId equals (Guid?)session.Id
         join contact in dbContext.Set<WhatsAppContact>() on outgoing.ContactId equals contact.Id
         where outgoing.Id == outgoingId && outgoing.Kind == WhatsAppOutgoingMessageKind.Flow
             && outgoing.ContactId == session.ContactId && outgoing.OperatorUserId == session.OperatorUserId
         select new WhatsAppFormDispatchRow(outgoing.Id, session.Id, session.TokenHash, session.ContactId, contact.WaId, session.UserId, session.OperatorUserId,
             session.DefinitionId, session.VersionId, session.BindingId, session.Culture, session.ProtectedPayload, session.ExpiresAtUtc,
             outgoing.State, outgoing.Body, contact.Mode, contact.ModeRevision, contact.LastInboundAtUtc))
        .SingleOrDefaultAsync(cancellationToken);

    public Task<WhatsAppFlowReplySessionRow?> FindReplySessionAsync(Guid contactId, string tokenHash, CancellationToken cancellationToken) =>
        (from session in dbContext.Set<WhatsAppFlowSession>().AsNoTracking()
         join contact in dbContext.Set<WhatsAppContact>().AsNoTracking() on session.ContactId equals contact.Id
         join account in dbContext.Users.AsNoTracking() on contact.UserId equals (Guid?)account.Id into accounts
         from account in accounts.DefaultIfEmpty()
         join binding in dbContext.Set<WhatsAppFlowBinding>().AsNoTracking()
             on new { session.BindingId, session.VersionId } equals new { BindingId = binding.Id, binding.VersionId }
         join operation in dbContext.Set<WhatsAppFlowOperation>().AsNoTracking() on binding.OperationId equals operation.Id
         where session.ContactId == contactId && session.TokenHash == tokenHash
         select new WhatsAppFlowReplySessionRow(session.Id, session.ContactId, session.UserId,
             session.DefinitionId, session.VersionId, binding.Id, operation.Id, binding.Culture,
             binding.ProtectedProjection, session.Prefix, session.TokenHash, session.ProtectedPayload,
             session.ExpiresAtUtc, session.LinkRevision == contact.LinkRevision && account != null
                 && account.Id == session.UserId && account.IsActive, session.CaptureState, session.CaptureErrorCode, session.RejectedQuestion))
        .SingleOrDefaultAsync(cancellationToken);
}
