using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppFlowStatusService
{
    Task<Result<StructuredFormPublicationResponse>> GetAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken);
    Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken);
}
