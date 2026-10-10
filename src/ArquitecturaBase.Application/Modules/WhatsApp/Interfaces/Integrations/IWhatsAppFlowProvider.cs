using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

public interface IWhatsAppFlowProvider
{
    Task<Result<bool>> VerifyPhoneAsync(string wabaId, string phoneNumberId, CancellationToken cancellationToken);
    Task<WhatsAppFlowEffectResponse> CreateAsync(WhatsAppFlowAccountContext account, string technicalName, CancellationToken cancellationToken);
    Task<WhatsAppFlowEffectResponse> UploadAsync(WhatsAppFlowAccountContext account, string metaFlowId, string json, CancellationToken cancellationToken);
    Task<WhatsAppFlowEffectResponse> PublishAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken);
    Task<WhatsAppFlowVerificationResponse> VerifyAsync(WhatsAppFlowAccountContext account, string metaFlowId, string technicalName, string expectedHash, CancellationToken cancellationToken);
    Task<WhatsAppFlowVerificationResponse> InspectUnknownCreationAsync(WhatsAppFlowAccountContext account, string technicalName, CancellationToken cancellationToken);
    Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken);
}
