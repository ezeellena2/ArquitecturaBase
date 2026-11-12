using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Channels;

internal sealed class UnavailableStructuredAttachmentReader : IStructuredAttachmentReader
{
    public Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StructuredAttachmentAccess.None);
    }

    public Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Unavailable));
    }
}
