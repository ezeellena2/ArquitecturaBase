using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
internal sealed class WhatsAppRetentionBatchScenario : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    public Queue<int> Batches { get; } = new([500,500,12,0]);
    public List<Guid> RepositoryInstances { get; } = [];
    public int Transactions { get; private set; }
    public int DisposedRepositories { get; private set; }
    public WhatsAppMessageRetentionService Service { get; }
    public WhatsAppRetentionBatchScenario()
    {
        var clock=new FakeTimeProvider(new DateTimeOffset(2026,10,18,12,0,0,TimeSpan.Zero));
        provider=new ServiceCollection().AddSingleton(this).AddSingleton<TimeProvider>(clock)
            .AddScoped<IWhatsAppMessageRetentionRepository,Repository>().AddScoped<Transaction>().AddScoped<IUnitOfWork>(services=>services.GetRequiredService<Transaction>())
            .AddScoped<IWhatsAppAssistantContentRetentionService>(services=>new WhatsAppAssistantContentRetentionService(
                services.GetRequiredService<IWhatsAppMessageRetentionRepository>(),services.GetRequiredService<IUnitOfWork>(),clock,
                NullLogger<WhatsAppAssistantContentRetentionService>.Instance)).BuildServiceProvider();
        Service=new(provider.GetRequiredService<IServiceScopeFactory>(),Options.Create(new WhatsAppMessageRetentionOptions()),clock,
            NullLogger<WhatsAppMessageRetentionService>.Instance);
    }
    public async ValueTask DisposeAsync(){Service.Dispose();await provider.DisposeAsync();}
    private sealed class Repository(WhatsAppRetentionBatchScenario host,Transaction transaction) : IWhatsAppMessageRetentionRepository,IDisposable
    {
        private readonly Guid id=Guid.NewGuid();
        public Task<int> ClearExpiredBodiesAsync(DateTime cutoffUtc,CancellationToken ct)
        {Assert.False(transaction.Open);return Task.FromResult(0);}
        public Task<int> ClearExpiredAssistantContentAsync(DateTime nowUtc,int limit,CancellationToken ct)
        {Assert.True(transaction.Open);Assert.Equal(500,limit);host.RepositoryInstances.Add(id);return Task.FromResult(host.Batches.Dequeue());}
        public void Dispose()=>host.DisposedRepositories++;
    }
    private sealed class Transaction(WhatsAppRetentionBatchScenario host) : IUnitOfWork
    {
        public bool Open {get;private set;}
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken,Task<T>> work,CommitPolicy policy,CancellationToken ct) where T:Result
        {Assert.False(Open);Open=true;host.Transactions++;try{return await work(ct);}finally{Open=false;}}
    }
}
