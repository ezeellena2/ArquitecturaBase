using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

internal sealed class BusinessFormRetirementSource(IFormDraftRepository drafts, IFormUnusedVersionReader reader) : IStructuredFormRetirementSource
{
    public Task LockSelectionAsync(CancellationToken cancellationToken) => drafts.LockDefinitionsAsync(cancellationToken);
    public Task<IReadOnlyList<StructuredFormVersionReference>> ListUnusedAsync(IReadOnlyCollection<StructuredFormVersionReference> candidates,
        CancellationToken cancellationToken) => reader.ListUnusedAsync(candidates, cancellationToken);
}
