using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// Home and a channel read a file attached to an assistant conversation without naming its owner: what a person may attach, and
/// the file prepared for the engine (the first sheet of a spreadsheet, or a document) or why it cannot be read and what it was. It
/// only reads: nothing is stored.
/// </summary>
public interface IStructuredAttachmentReader
{
    /// <summary>
    /// Whether the named account may attach spreadsheets and documents, each apart; never the current user: a channel runs in a worker.
    /// </summary>
    Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken);

    /// <summary>The kind comes from the bytes, never from the extension of the name.</summary>
    Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken);
}
