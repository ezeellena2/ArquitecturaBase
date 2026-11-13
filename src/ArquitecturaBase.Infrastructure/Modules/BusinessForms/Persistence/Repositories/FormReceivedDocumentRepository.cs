using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormReceivedDocumentRepository(ApplicationDbContext dbContext) : IFormReceivedDocumentRepository
{
    public Task LockSourceAsync(string channelId, string sourceId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.ReceivedDocumentSource(channelId, sourceId)], cancellationToken);

    public Task LockAsync(Guid documentId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.ReceivedDocument(documentId)], cancellationToken);

    public Task<FormReceivedDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken) =>
        dbContext.Set<FormReceivedDocument>().SingleOrDefaultAsync(document => document.Id == documentId, cancellationToken);

    public Task<FormReceivedDocument?> GetBySourceAsync(string channelId, string sourceId, CancellationToken cancellationToken) =>
        dbContext.Set<FormReceivedDocument>().SingleOrDefaultAsync(document => document.ChannelId == channelId && document.SourceId == sourceId
            && document.RowNumber == null, cancellationToken);

    public Task<bool> ExistsSourceAsync(string channelId, string sourceId, CancellationToken cancellationToken) =>
        dbContext.Set<FormReceivedDocument>().AnyAsync(document => document.ChannelId == channelId && document.SourceId == sourceId, cancellationToken);

    public void Add(FormReceivedDocument document)
    {
        dbContext.RequireTransaction();
        dbContext.Set<FormReceivedDocument>().Add(document);
    }

    public void Add(FormReceivedDocumentEvent documentEvent)
    {
        dbContext.RequireTransaction();
        dbContext.Set<FormReceivedDocumentEvent>().Add(documentEvent);
    }

    public void Remove(FormReceivedDocument document)
    {
        dbContext.RequireTransaction();
        dbContext.Set<FormReceivedDocument>().Remove(document);
    }
}
