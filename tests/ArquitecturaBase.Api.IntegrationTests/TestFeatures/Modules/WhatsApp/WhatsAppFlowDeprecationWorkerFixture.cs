using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppFlowDeprecationWorkerFixture : IWhatsAppFlowDeprecationService, IWhatsAppFlowEnvironment, IWhatsAppFlowProvider
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026,10,21,12,0,0,TimeSpan.Zero));
    public List<string> Events { get; } = [];
    public bool Verify { get; init; }
    public bool DifferentAccount { get; init; }
    public bool ThrowAfterPost { get; init; }
    public CancellationTokenSource? CancelAfterPost { get; init; }
    public WhatsAppFlowEffectResponse? Effect { get; private set; }
    public WhatsAppFlowStateResponse? Verification { get; private set; }
    public bool CompletionWasCancelled { get; private set; }
    public bool IsConfigured => true;
    public TimeSpan SessionLifetime => TimeSpan.FromHours(1);
    private static WhatsAppFlowAccountContext Account => new("222","111","v25.0","configuration");
    public Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken ct)
    { Events.Add("Account"); return Task.FromResult<Result<WhatsAppFlowAccountContext>>(DifferentAccount ? new("999","111","v25.0","configuration") : Account); }
    public Task<IReadOnlyList<Guid>> ListDueBindingIdsAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<Result<WhatsAppFlowDeprecationWorkResponse?>> ReserveAsync(Guid? id, CancellationToken ct)
    { Events.Add("Reserve"); return Task.FromResult<Result<WhatsAppFlowDeprecationWorkResponse?>>(new WhatsAppFlowDeprecationWorkResponse(Guid.NewGuid(),Guid.NewGuid(),id!.Value,Guid.NewGuid(),Guid.NewGuid(),1,
        Verify ? WhatsAppFlowDeprecationStep.Verify : WhatsAppFlowDeprecationStep.Deprecate,"333","fixed",new(2,"request","publication",Account,[]),Clock.GetUtcNow().UtcDateTime.AddMinutes(2))); }
    public Task<Result> MarkEffectStartedAsync(WhatsAppFlowDeprecationWorkResponse work, CancellationToken ct)
    { Events.Add("Mark"); return Task.FromResult(Result.Success()); }
    public Task<Result> CompleteEffectAsync(WhatsAppFlowDeprecationWorkResponse work, WhatsAppFlowEffectResponse response, CancellationToken ct)
    { Events.Add("Complete"); Effect=response; CompletionWasCancelled=ct.IsCancellationRequested; return Task.FromResult(Result.Success()); }
    public Task<Result> CompleteVerificationAsync(WhatsAppFlowDeprecationWorkResponse work, WhatsAppFlowStateResponse response, CancellationToken ct)
    { Events.Add("Verified"); Verification=response; CompletionWasCancelled=ct.IsCancellationRequested; return Task.FromResult(Result.Success()); }
    public Task<WhatsAppFlowEffectResponse> DeprecateAsync(WhatsAppFlowAccountContext account,string id,CancellationToken ct)
    { Events.Add("POST"); Assert.Contains("Mark",Events); CancelAfterPost?.Cancel(); if(ThrowAfterPost) throw new OperationCanceledException(ct); return Task.FromResult(new WhatsAppFlowEffectResponse(true,false,null,[])); }
    public Task<WhatsAppFlowStateResponse> GetStateAsync(WhatsAppFlowAccountContext account,string id,string name,CancellationToken ct)
    { Events.Add("GET"); return Task.FromResult(new WhatsAppFlowStateResponse(true,WhatsAppFlowRemoteState.Deprecated)); }
    public Task<Result<bool>> VerifyPhoneAsync(string wabaId,string phoneId,CancellationToken ct)=>throw new NotSupportedException();
    public Task<WhatsAppFlowEffectResponse> CreateAsync(WhatsAppFlowAccountContext account,string name,CancellationToken ct)=>throw new NotSupportedException();
    public Task<WhatsAppFlowEffectResponse> UploadAsync(WhatsAppFlowAccountContext account,string id,string json,CancellationToken ct)=>throw new NotSupportedException();
    public Task<WhatsAppFlowEffectResponse> PublishAsync(WhatsAppFlowAccountContext account,string id,CancellationToken ct)=>throw new NotSupportedException();
    public Task<WhatsAppFlowVerificationResponse> VerifyAsync(WhatsAppFlowAccountContext account,string id,string name,string hash,CancellationToken ct)=>throw new NotSupportedException();
    public Task<WhatsAppFlowVerificationResponse> InspectUnknownCreationAsync(WhatsAppFlowAccountContext account,string name,CancellationToken ct)=>throw new NotSupportedException();
    public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(WhatsAppFlowAccountContext account,string id,CancellationToken ct)=>throw new NotSupportedException();
}
