using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Channels;

internal sealed class DisabledStructuredFormSource : IStructuredFormSource
{
    public Task LockSelectionAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task<StructuredFormSelectionRow?> FindAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<StructuredFormSelectionRow?>(null);
    }
    public Task<StructuredFormSelectionRow?> FindAcceptedSessionAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<StructuredFormSelectionRow?>(null);
    }
    public Task<PagedResult<StructuredFormSelectionRow>> ListAsync(Guid recipientUserId, string recipientCulture,
        ListStructuredFormsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PagedResult<StructuredFormSelectionRow>([], request.Page, request.PageSize, 0));
    }
}
