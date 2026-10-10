using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;
internal sealed class DisabledWhatsAppFlowProvider : IWhatsAppFlowProvider
{
    public Task<bool> VerifyPhoneAsync(string wabaId, string phoneNumberId, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<WhatsAppFlowEffectResponse> CreateAsync(WhatsAppFlowAccountContext account, string technicalName, CancellationToken cancellationToken) => Task.FromResult(Unavailable());
    public Task<WhatsAppFlowEffectResponse> UploadAsync(WhatsAppFlowAccountContext account, string metaFlowId, string json, CancellationToken cancellationToken) => Task.FromResult(Unavailable());
    public Task<WhatsAppFlowEffectResponse> PublishAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken) => Task.FromResult(Unavailable());
    public Task<WhatsAppFlowVerificationResponse> VerifyAsync(WhatsAppFlowAccountContext account, string metaFlowId, string technicalName, string expectedHash, CancellationToken cancellationToken) => Task.FromResult(new WhatsAppFlowVerificationResponse(false, false, false, false, false));
    public Task<WhatsAppFlowVerificationResponse> InspectUnknownCreationAsync(WhatsAppFlowAccountContext account, string technicalName, CancellationToken cancellationToken) => Task.FromResult(new WhatsAppFlowVerificationResponse(false, false, false, false, false));
    public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken) => Task.FromResult<Result<StructuredFormPreviewResponse>>(FlowErrors.PreviewUnavailable);
    private static WhatsAppFlowEffectResponse Unavailable() => new(false, false, null, [], FlowErrors.AccountUnavailable.Code);
}
