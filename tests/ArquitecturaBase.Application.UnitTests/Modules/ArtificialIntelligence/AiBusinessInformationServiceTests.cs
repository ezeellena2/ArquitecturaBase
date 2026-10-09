using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;
public sealed class AiBusinessInformationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Identical_retries_return_the_original_revision_after_a_later_edit()
    {
        using var host = new Host();
        var request = Request();
        var saved = await host.Service.UpdateAsync(request, Ct);
        Assert.True(saved.IsSuccess);
        Assert.True((await host.Service.UpdateAsync(Request() with { ExpectedRevision = 1, Name = "Later" }, Ct)).IsSuccess);
        var retry = await host.Service.UpdateAsync(request, Ct);
        Assert.Equal(saved.Value, retry.Value);
        Assert.Equal(2, host.Store.Information!.Revision);
        Assert.Equal(2, host.Store.Changes.Count);
        Assert.Equal(3, host.Work.Transactions);
    }
    [Fact]
    public async Task Reusing_an_identifier_for_different_facts_does_not_overwrite_the_business()
    {
        using var host = new Host();
        var request = Request();
        Assert.True((await host.Service.UpdateAsync(request, Ct)).IsSuccess);
        var result = await host.Service.UpdateAsync(request with { Hours = "Different" }, Ct);
        Assert.Equal(AiBusinessInformationErrors.RequestConflict, result.Error);
        Assert.Equal(1, host.Store.Information!.Revision);
    }
    [Fact]
    public async Task A_stale_revision_requires_review_and_preserves_saved_facts()
    {
        using var host = new Host();
        Assert.True((await host.Service.UpdateAsync(Request(), Ct)).IsSuccess);
        var result = await host.Service.UpdateAsync(Request() with { Name = "Stale" }, Ct);
        Assert.Equal(AiBusinessInformationErrors.RevisionConflict, result.Error);
        Assert.Equal("Shop", host.Store.Information!.Name);
    }
    [Fact]
    public async Task Validation_returns_fields_before_opening_a_transaction()
    {
        using var host = new Host();
        var result = await host.Service.UpdateAsync(Request() with { Name = "", Description = "", RequestId = Guid.Empty }, Ct);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("name", error.Errors.Keys);
        Assert.Contains("description", error.Errors.Keys);
        Assert.Equal(0, host.Work.Transactions);
    }
    [Fact]
    public async Task Reading_empty_information_does_not_create_a_row_or_open_a_transaction()
    {
        using var host = new Host();
        var result = await host.Service.GetAsync(Ct);
        Assert.Equal(AiBusinessInformationResponse.Empty, result.Value);
        Assert.Null(host.Store.Information);
        Assert.Equal(0, host.Work.Transactions);
    }
    [Fact]
    public async Task Receipts_are_owned_by_the_operator_and_contain_only_revision()
    {
        using var host = new Host();
        var request = Request();
        Assert.True((await host.Service.UpdateAsync(request, Ct)).IsSuccess);
        var result = await host.Service.GetChangeAsync(request.RequestId, Ct);
        Assert.Equal(1, result.Value.Revision);
        Assert.DoesNotContain("Shop", result.Value.ToString(), StringComparison.Ordinal);
        host.User.UserId = Guid.NewGuid();
        Assert.Equal(AiBusinessInformationErrors.ChangeNotFound, (await host.Service.GetChangeAsync(request.RequestId, Ct)).Error);
        Assert.Equal(1, host.Work.Transactions);
    }
    private static UpdateAiBusinessInformationRequest Request() => new("Shop", "Repairs", "", "", "", "", "", 0, Guid.NewGuid());
    private sealed class Host : IDisposable
    {
        public FakeUnitOfWork Work { get; } = new();
        public Store Store { get; } = new();
        public User User { get; } = new();
        private ServiceProvider Provider { get; }
        public IAiBusinessInformationService Service => Provider.GetRequiredService<IAiBusinessInformationService>();
        public Host()
        {
            var services = new ServiceCollection().AddLogging().AddApplication().AddArtificialIntelligenceApplication();
            services.AddSingleton<IUnitOfWork>(Work);
            services.AddSingleton<TimeProvider>(new FakeTimeProvider());
            services.AddSingleton<IAiBusinessInformationRepository>(Store);
            services.AddSingleton<IAiBusinessInformationReader>(Store);
            services.AddSingleton<IAiBusinessInformationFingerprint, Fingerprint>();
            services.AddSingleton<ICurrentUser>(User);
            Store.InTransaction = () => Work.InTransaction;
            Provider = services.BuildServiceProvider();
        }
        public void Dispose() => Provider.Dispose();
    }
    private sealed class User : ICurrentUser
    {
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public bool IsAuthenticated => UserId.HasValue;
    }
    private sealed class Fingerprint : IAiBusinessInformationFingerprint
    {
        public string ActiveKeyId => "fake";
        public string Create(UpdateAiBusinessInformationRequest request, string keyId) =>
            string.Join('|', request.Name.Trim(), request.Description.Trim(), request.Hours.Trim(), request.Location.Trim(),
                request.Services.Trim(), request.Policies.Trim(), request.Faq.Trim(), request.ExpectedRevision, request.RequestId);
        public bool Matches(UpdateAiBusinessInformationRequest request, string keyId, string fingerprint) => Create(request, keyId) == fingerprint;
    }
    private sealed class Store : IAiBusinessInformationRepository, IAiBusinessInformationReader
    {
        public Func<bool> InTransaction { get; set; } = () => false;
        public AiBusinessInformation? Information { get; private set; }
        public List<AiBusinessInformationChange> Changes { get; } = [];
        public Task LockAsync(CancellationToken cancellationToken) { Assert.True(InTransaction()); return Task.CompletedTask; }
        public Task<AiBusinessInformation?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Information);
        public Task<AiBusinessInformationChange?> GetChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Changes.SingleOrDefault(x => x.OperatorId == operatorId && x.RequestId == requestId));
        public void Add(AiBusinessInformation information) { Assert.True(InTransaction()); Information = information; }
        public void AddChange(AiBusinessInformationChange change) { Assert.True(InTransaction()); Changes.Add(change); }
        public Task DeleteExpiredChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AiBusinessInformationResponse> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Information is { } x
            ? new AiBusinessInformationResponse(x.Name, x.Description, x.Hours, x.Location, x.Services, x.Policies, x.Faq, x.Revision)
            : AiBusinessInformationResponse.Empty);
        public Task<AiBusinessInformationChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Changes.Where(x => x.OperatorId == operatorId && x.RequestId == requestId)
                .Select(x => new AiBusinessInformationChangeResponse(x.RequestId, x.Revision)).SingleOrDefault());
    }
}
