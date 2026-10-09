using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiConnectionServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Listing_models_uses_a_candidate_key_without_requiring_a_model_or_saving()
    {
        using var host = new Host();
        var service = host.Provider.GetRequiredService<IAiConnectionTestService>();
        var result = await service.ListModelsAsync(new(AiProvider.Anthropic, AiCredentialChange.Replace, "fake-key", 0), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Revision);
        Assert.Equal("model", Assert.Single(result.Value.Models).Id);
        Assert.Equal(AiProvider.Anthropic, host.Client.ModelsRequest!.Provider);
        Assert.Equal("fake-key", host.Client.ModelsRequest.ApiKey);
        Assert.Equal(0, host.Secrets.Encryptions);
        Assert.Equal(0, host.Work.Transactions);
        Assert.Null(host.Store.Connection);
    }

    [Fact]
    public async Task Listing_models_reveals_only_the_matching_saved_key_and_rejects_stale_revisions()
    {
        using var host = new Host();
        await host.Service.UpdateAsync(Request(), Ct);
        var service = host.Provider.GetRequiredService<IAiConnectionTestService>();
        Assert.True((await service.ListModelsAsync(new(AiProvider.OpenAI, AiCredentialChange.Keep, null, 1), Ct)).IsSuccess);
        Assert.Equal("fake-key", host.Client.ModelsRequest!.ApiKey);
        Assert.Equal(AiConnectionErrors.ProviderChanged, (await service.ListModelsAsync(new(AiProvider.Anthropic, AiCredentialChange.Keep, null, 1), Ct)).Error);
        Assert.Equal(AiConnectionErrors.RevisionConflict, (await service.ListModelsAsync(new(AiProvider.OpenAI, AiCredentialChange.Keep, null, 0), Ct)).Error);
        Assert.Equal(1, host.Client.ModelCalls);
        Assert.Equal(1, host.Work.Transactions);
    }

    [Theory]
    [InlineData(AiCredentialChange.Replace, "fake\r\nkey")]
    [InlineData(AiCredentialChange.Replace, "")]
    [InlineData(AiCredentialChange.Keep, "fake-key")]
    [InlineData(AiCredentialChange.Remove, null)]
    public async Task Invalid_catalog_credentials_are_rejected_before_calling_the_provider(AiCredentialChange change, string? key)
    {
        using var host = new Host();
        var result = await host.Provider.GetRequiredService<IAiConnectionTestService>()
            .ListModelsAsync(new(AiProvider.OpenAI, change, key, 0), Ct);
        Assert.IsType<ArquitecturaBase.Domain.Results.ValidationError>(result.Error);
        Assert.Equal(0, host.Client.ModelCalls);
    }

    [Fact]
    public async Task Testing_a_candidate_does_not_encrypt_or_persist_it()
    {
        using var host = new Host();
        var test = host.Provider.GetRequiredService<IAiConnectionTestService>();
        var result = await test.TestAsync(new(AiProvider.OpenAI, "model", AiCredentialChange.Replace, "fake-key", 0), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, host.Secrets.Encryptions);
        Assert.Equal(0, host.Work.Transactions);
        Assert.Null(host.Store.Connection);
    }

    [Fact]
    public async Task Retrying_an_identical_save_returns_the_receipt_without_advancing_revision()
    {
        using var host = new Host();
        var request = Request();
        var first = await host.Service.UpdateAsync(request, Ct);
        var retry = await host.Service.UpdateAsync(request, Ct);
        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value, retry.Value);
        Assert.Equal(1, host.Store.Connection!.Revision);
        Assert.Single(host.Store.Changes);
        Assert.Equal(2, host.Work.Transactions);
    }

    [Fact]
    public async Task A_reused_identifier_with_a_different_secret_is_rejected()
    {
        using var host = new Host();
        var request = Request();
        await host.Service.UpdateAsync(request, Ct);
        var result = await host.Service.UpdateAsync(request with { ApiKey = "another-key" }, Ct);
        Assert.Equal(AiConnectionErrors.RequestConflict, result.Error);
        Assert.Equal(1, host.Store.Connection!.Revision);
    }

    [Fact]
    public async Task A_stale_revision_and_a_provider_change_do_not_replace_the_previous_secret()
    {
        using var host = new Host();
        await host.Service.UpdateAsync(Request(), Ct);
        var result = await host.Service.UpdateAsync(Request() with { ExpectedRevision = 0 }, Ct);
        Assert.Equal(AiConnectionErrors.RevisionConflict, result.Error);
        result = await host.Service.UpdateAsync(Request() with
            { ExpectedRevision = 1, Provider = AiProvider.Anthropic, CredentialChange = AiCredentialChange.Keep, ApiKey = null }, Ct);
        Assert.Equal(AiConnectionErrors.ProviderChanged, result.Error);
        Assert.Equal("protected:fake-key", host.Store.Connection!.ProtectedApiKey);
    }

    [Fact]
    public async Task Validation_rejects_header_injection_before_opening_a_transaction()
    {
        using var host = new Host();
        var result = await host.Service.UpdateAsync(Request() with { ApiKey = "fake\r\nAuthorization: other" }, Ct);
        Assert.IsType<ArquitecturaBase.Domain.Results.ValidationError>(result.Error);
        Assert.Equal(0, host.Work.Transactions);
    }

    [Fact]
    public async Task Testing_does_not_save_and_does_not_send_a_saved_secret_to_another_provider()
    {
        using var host = new Host();
        await host.Service.UpdateAsync(Request(), Ct);
        var test = host.Provider.GetRequiredService<IAiConnectionTestService>();
        var result = await test.TestAsync(new(AiProvider.OpenAI, "model", AiCredentialChange.Keep, null, 1), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, host.Work.Transactions);
        result = await test.TestAsync(new(AiProvider.Anthropic, "model", AiCredentialChange.Keep, null, 1), Ct);
        Assert.Equal(AiConnectionErrors.ProviderChanged, result.Error);
        Assert.Equal(1, host.Client.Calls);
    }

    private static UpdateAiConnectionRequest Request() => new(AiProvider.OpenAI, "model", AiCredentialChange.Replace,
        "fake-key", true, 0, Guid.NewGuid());

    private sealed class Host : IDisposable
    {
        public FakeUnitOfWork Work { get; } = new();
        public Store Store { get; } = new();
        public Client Client { get; } = new();
        public Protector Secrets { get; } = new();
        public ServiceProvider Provider { get; }
        public IAiConnectionService Service => Provider.GetRequiredService<IAiConnectionService>();
        public Host()
        {
            var services = new ServiceCollection().AddLogging().AddApplication().AddArtificialIntelligenceApplication();
            services.AddSingleton<ArquitecturaBase.Application.Interfaces.Persistence.IUnitOfWork>(Work);
            services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
            services.AddSingleton<IAiConnectionRepository>(Store);
            services.AddSingleton<IAiConnectionReader>(Store);
            services.AddSingleton<IAiSecretProtector>(Secrets);
            services.AddSingleton<IAiChangeFingerprint, Fingerprint>();
            services.AddSingleton<ICurrentUser, User>();
            services.AddSingleton<ILlmClient>(Client);
            Store.InTransaction = () => Work.InTransaction;
            Provider = services.BuildServiceProvider();
        }
        public void Dispose() => Provider.Dispose();
    }
    private sealed class User : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
    }
    private sealed class Protector : IAiSecretProtector
    {
        public int Encryptions { get; private set; }
        public string Protect(AiProvider provider, string apiKey) { Encryptions++; return "protected:" + apiKey; }
        public string? Unprotect(AiProvider provider, string protectedApiKey) => protectedApiKey[10..];
    }
    private sealed class Fingerprint : IAiChangeFingerprint
    {
        public string ActiveKeyId => "test";
        public string Create(UpdateAiConnectionRequest request, string keyId) =>
            string.Join('|', request.Provider, request.Model, request.CredentialChange, request.ApiKey, request.Enabled, request.ExpectedRevision, request.RequestId);
        public bool Matches(UpdateAiConnectionRequest request, string keyId, string fingerprint) => Create(request, keyId) == fingerprint;
    }
    private sealed class Client : ILlmClient
    {
        public int Calls { get; private set; }
        public int ModelCalls { get; private set; }
        public LlmModelsRequest? ModelsRequest { get; private set; }
        public Task<ArquitecturaBase.Domain.Results.Result<IReadOnlyList<AiModelResponse>>> ListModelsAsync(LlmModelsRequest request, CancellationToken cancellationToken)
        {
            ModelCalls++;
            ModelsRequest = request;
            return Task.FromResult(ArquitecturaBase.Domain.Results.Result.Success<IReadOnlyList<AiModelResponse>>([new("model", "Model")]));
        }
        public Task<ArquitecturaBase.Domain.Results.Result<LlmTestResponse>> TestAsync(LlmTestRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ArquitecturaBase.Domain.Results.Result.Success(new LlmTestResponse(4, 1)));
        }
    }
    private sealed class Store : IAiConnectionRepository, IAiConnectionReader
    {
        public Func<bool> InTransaction { get; set; } = () => false;
        public AiConnection? Connection { get; private set; }
        public List<AiConnectionChange> Changes { get; } = [];
        public Task LockAsync(CancellationToken cancellationToken)
        {
            Assert.True(InTransaction());
            return Task.CompletedTask;
        }
        public Task<AiConnection?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Connection);
        public Task<AiConnectionChange?> GetChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Changes.SingleOrDefault(x => x.OperatorId == operatorId && x.RequestId == requestId));
        public void Add(AiConnection connection) { Assert.True(InTransaction()); Connection = connection; }
        public void AddChange(AiConnectionChange change) { Assert.True(InTransaction()); Changes.Add(change); }
        public Task DeleteExpiredChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AiConnectionSnapshot> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Connection is { } c
            ? new AiConnectionSnapshot(c.Provider, c.Model, c.ProtectedApiKey, c.Enabled, c.Revision)
            : new AiConnectionSnapshot(AiProvider.OpenAI, "", null, false, 0));
        public Task<AiConnectionChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult<AiConnectionChangeResponse?>(null);
    }
}
