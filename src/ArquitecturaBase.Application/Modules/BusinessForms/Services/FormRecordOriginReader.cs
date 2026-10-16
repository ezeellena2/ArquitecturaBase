using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormRecordOriginReader(IEnumerable<IStructuredRecordOriginSource> sources)
{
    public async Task<Dictionary<Guid, StructuredRecordOrigin>> ListAsync(Guid actor, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, StructuredRecordOrigin>();
        foreach (var source in sources)
            foreach (var origin in await source.ListAsync(actor, ids, cancellationToken))
                result.Add(origin.SourceReceiptId, origin);
        return result;
    }
}
