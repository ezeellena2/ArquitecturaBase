using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
internal sealed class UnusedFlowPublicationServices(Error? acceptanceError = null) : IWhatsAppFlowPublicationService, IWhatsAppFlowStatusService
{
    public Task<Result<StructuredFormPublicationResponse>> AcceptAsync(StructuredFormPublicationRequest request, CancellationToken cancellationToken) => acceptanceError is null ? throw new NotSupportedException() : Task.FromResult<Result<StructuredFormPublicationResponse>>(acceptanceError);
    public Task<Result<StructuredFormPublicationResponse>> GetAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
}
