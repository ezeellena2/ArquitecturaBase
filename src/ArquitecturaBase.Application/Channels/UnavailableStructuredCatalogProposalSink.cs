using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Application.Channels;

internal sealed class UnavailableStructuredCatalogProposalSink : IStructuredCatalogProposalSink
{
    public Task<Result<StructuredCatalogProposalAcceptedResponse>> AcceptAsync(StructuredCatalogProposalAcceptanceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Result<StructuredCatalogProposalAcceptedResponse>>(SystemQueryErrors.NotFound);
    }
}
