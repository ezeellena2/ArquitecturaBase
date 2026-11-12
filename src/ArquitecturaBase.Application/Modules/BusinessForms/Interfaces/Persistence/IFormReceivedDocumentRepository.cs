using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>
/// The inbox «Documentos recibidos» to change it: tracked documents and their history, never saved here. <see cref="LockSourceAsync"/>
/// serializes registering one channel source id; <see cref="LockAsync"/>, every change to one document, the last lock taken.
/// </summary>
public interface IFormReceivedDocumentRepository
{
    Task LockSourceAsync(string channelId, string sourceId, CancellationToken cancellationToken);
    Task LockAsync(Guid documentId, CancellationToken cancellationToken);
    Task<FormReceivedDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken);
    Task<FormReceivedDocument?> GetBySourceAsync(string channelId, string sourceId, CancellationToken cancellationToken);
    void Add(FormReceivedDocument document);
    void Add(FormReceivedDocumentEvent documentEvent);
    /// <summary>Deletes the document; its history goes with it.</summary>
    void Remove(FormReceivedDocument document);
}
