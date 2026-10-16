using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Application.Interfaces.Channels;
/// <summary>Optional channel contribution to the origin of already-authorized records. No values or transport tokens.</summary>
public interface IStructuredRecordOriginSource
{
    Task<IReadOnlyList<StructuredRecordOrigin>> ListAsync(Guid actorUserId, IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken);
}
