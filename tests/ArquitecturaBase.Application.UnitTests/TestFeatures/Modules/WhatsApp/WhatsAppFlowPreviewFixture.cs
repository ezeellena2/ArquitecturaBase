using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

public enum WhatsAppFlowPreviewHang
{
    None,
    Account,
    Verification,
    Preview,
}

/// <summary>
/// One published Flow with a single culture and a fake Meta that can stop answering at one call of the official preview.
/// </summary>
internal sealed class WhatsAppFlowPreviewFixture : IWhatsAppFlowReader, IWhatsAppFlowProtector, IWhatsAppFlowProvider, IWhatsAppFlowEnvironment
{
    private static readonly WhatsAppFlowAccountContext Account = new("waba", "phone", "v25.0", "configuration");
    private readonly WhatsAppFlowOperationRow _row;

    public WhatsAppFlowPreviewFixture()
    {
        _row = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), [1], null,
            [new(Guid.CreateVersion7(), "es", "form", [2], "333", "Published", "Verify", null, "[]")]);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    public WhatsAppFlowPreviewHang Hang { get; set; }
    public bool IsConfigured => true;
    public TimeSpan SessionLifetime => TimeSpan.FromHours(24);

    public StructuredFormPreviewQuery Query => new(_row.NeutralOperationId, _row.DefinitionId, _row.VersionId, "fingerprint", "es");

    public WhatsAppFlowStatusService CreateService() =>
        new(this, this, this, this, Time, NullLogger<WhatsAppFlowStatusService>.Instance);

    public Task<WhatsAppFlowOperationRow?> FindAsync(Guid neutralOperationId, CancellationToken cancellationToken) =>
        Task.FromResult<WhatsAppFlowOperationRow?>(neutralOperationId == _row.NeutralOperationId ? _row : null);

    public WhatsAppFlowStoredManifest ReadManifest(Guid operationId, Guid neutralOperationId, Guid versionId, byte[] protectedManifest) =>
        new(2, "request", "fingerprint", Account, ["forms.read"]);

    public WhatsAppFlowProjection ReadProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, byte[] protectedProjection) =>
        new(1, """{"screens":[{"id":"FORM"}]}""", "hash", WhatsAppFlowCompiler.ProfileRevision, 1, culture, []);

    public Result<WhatsAppFlowAccountContext> ConfiguredAccount() => Account;

    public async Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken cancellationToken)
    {
        await StopIfAsync(WhatsAppFlowPreviewHang.Account, cancellationToken);
        return Account;
    }

    public async Task<WhatsAppFlowVerificationResponse> VerifyAsync(WhatsAppFlowAccountContext account, string metaFlowId, string technicalName,
        string expectedHash, CancellationToken cancellationToken)
    {
        await StopIfAsync(WhatsAppFlowPreviewHang.Verification, cancellationToken);
        return new(true, false, true, true, true);
    }

    public async Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(WhatsAppFlowAccountContext account, string metaFlowId,
        WhatsAppFlowPreviewStart start, CancellationToken cancellationToken)
    {
        await StopIfAsync(WhatsAppFlowPreviewHang.Preview, cancellationToken);
        return new StructuredFormPreviewResponse("https://business.facebook.com/wa/manage/flows/333/preview/", Time.GetUtcNow().UtcDateTime.AddMinutes(5));
    }

    private Task StopIfAsync(WhatsAppFlowPreviewHang stage, CancellationToken cancellationToken) =>
        Hang == stage ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.CompletedTask;

    public Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guid>> ListDueDeprecationBindingsAsync(DateTime nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<WhatsAppFlowDeprecationCandidateRow?> FindDeprecationAsync(Guid bindingId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> ExistsOpenVersionWorkAsync(Guid versionId, DateTime nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    public byte[] ProtectManifest(Guid operationId, Guid neutralOperationId, Guid versionId, WhatsAppFlowStoredManifest manifest) => throw new NotSupportedException();
    public byte[] ProtectProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, WhatsAppFlowProjection projection) => throw new NotSupportedException();
    public Task<Result<bool>> VerifyPhoneAsync(string wabaId, string phoneNumberId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<WhatsAppFlowEffectResponse> CreateAsync(WhatsAppFlowAccountContext account, string technicalName, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<WhatsAppFlowEffectResponse> UploadAsync(WhatsAppFlowAccountContext account, string metaFlowId, string json, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<WhatsAppFlowEffectResponse> PublishAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<WhatsAppFlowEffectResponse> DeprecateAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<WhatsAppFlowStateResponse> GetStateAsync(WhatsAppFlowAccountContext account, string metaFlowId, string technicalName, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<WhatsAppFlowVerificationResponse> InspectUnknownCreationAsync(WhatsAppFlowAccountContext account, string technicalName, CancellationToken cancellationToken) => throw new NotSupportedException();
}
