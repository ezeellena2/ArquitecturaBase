using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Channels;

public interface IStructuredCatalogProposalSink
{
    Task<Result<StructuredCatalogProposalAcceptedResponse>> AcceptAsync(
        StructuredCatalogProposalAcceptanceRequest request, CancellationToken cancellationToken);
}
