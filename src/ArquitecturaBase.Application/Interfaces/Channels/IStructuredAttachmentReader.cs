using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// Home and a channel read a spreadsheet attached to an assistant conversation without naming its owner: whether a person may
/// attach one, and its first sheet prepared for the engine or why it cannot be read. It only reads: nothing is stored.
/// </summary>
public interface IStructuredAttachmentReader
{
    /// <summary>For the named account, never the current user: a channel runs in a worker.</summary>
    Task<bool> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken);

    /// <summary>The kind comes from the bytes, never from the extension of the name.</summary>
    Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken);
}
