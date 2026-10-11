using System.Collections.Concurrent;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
internal sealed class CapturingFlowProvider(TimeProvider timeProvider) : IWhatsAppFlowProvider
{
    private static long _sequence = 700;
    private readonly ConcurrentDictionary<string, (string Name, string? Json, bool Published)> _flows = new(StringComparer.Ordinal);
    public int Creates { get; private set; }
    public int Uploads { get; private set; }
    public int Publishes { get; private set; }
    public bool RejectUpload { get; set; }
    public bool OwnsPhone { get; set; } = true;
    public bool MembershipUnavailable { get; set; }
    public int MembershipLookups { get; private set; }
    public bool PublishUncertainOnce { get; set; }
    public string? LastPreviewFlowId { get; private set; }
    public TimeSpan PreviewLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public void SeedPublished(string flowId, string technicalName, string json) => _flows[flowId] = (technicalName, json, true);
    public Task<ArquitecturaBase.Domain.Results.Result<bool>> VerifyPhoneAsync(string wabaId, string phoneNumberId, CancellationToken cancellationToken)
    {
        MembershipLookups++;
        return Task.FromResult<ArquitecturaBase.Domain.Results.Result<bool>>(MembershipUnavailable
            ? FlowErrors.AccountUnavailable : OwnsPhone);
    }
    public Task<WhatsAppFlowEffectResponse> CreateAsync(WhatsAppFlowAccountContext account, string technicalName, CancellationToken cancellationToken)
    {
        Creates++; var id = Interlocked.Increment(ref _sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _flows[id] = (technicalName, null, false); return Task.FromResult(new WhatsAppFlowEffectResponse(true, false, id, []));
    }
    public Task<WhatsAppFlowEffectResponse> UploadAsync(WhatsAppFlowAccountContext account, string metaFlowId, string json, CancellationToken cancellationToken)
    {
        Uploads++; var existing = _flows[metaFlowId]; _flows[metaFlowId] = (existing.Name, json, false);
        return Task.FromResult(new WhatsAppFlowEffectResponse(true, false, null, RejectUpload ? [new(0, null, "label")] : []));
    }
    public Task<WhatsAppFlowEffectResponse> PublishAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken)
    {
        Publishes++; var existing = _flows[metaFlowId]; _flows[metaFlowId] = (existing.Name, existing.Json, true);
        var uncertain = PublishUncertainOnce; PublishUncertainOnce = false;
        return Task.FromResult(new WhatsAppFlowEffectResponse(!uncertain, uncertain, null, []));
    }
    public Task<WhatsAppFlowVerificationResponse> VerifyAsync(WhatsAppFlowAccountContext account, string metaFlowId, string technicalName, string expectedHash, CancellationToken cancellationToken)
    {
        var existing = _flows[metaFlowId];
        var matches = existing.Name == technicalName && existing.Json is not null && Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(existing.Json))) == expectedHash;
        return Task.FromResult(new WhatsAppFlowVerificationResponse(existing.Published, !existing.Published, matches, !RejectUpload, true));
    }
    public Task<WhatsAppFlowVerificationResponse> InspectUnknownCreationAsync(WhatsAppFlowAccountContext account, string technicalName, CancellationToken cancellationToken) => Task.FromResult(new WhatsAppFlowVerificationResponse(false, false, false, false, false, _flows.Count(flow => flow.Value.Name == technicalName)));
    public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken)
    {
        LastPreviewFlowId = metaFlowId;
        return Task.FromResult<Result<StructuredFormPreviewResponse>>(new StructuredFormPreviewResponse("https://business.facebook.com/wa/manage/flows/" + metaFlowId + "/preview/?token=private", timeProvider.GetUtcNow().UtcDateTime.Add(PreviewLifetime)));
    }
}
