using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantRetentionTests
{
    [Fact]
    public async Task The_existing_retention_job_clears_assistant_content_with_one_tracked_transaction_and_preserves_legacy_payload()
    {
        var now=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);var transaction=new Transaction();
        var session=WhatsAppFlowSession.Create(Guid.NewGuid(),0,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),1,Guid.NewGuid(),
            "es","{\"es\":\"Visita\"}","VIS",new string('a',64),new string('b',64),now.AddHours(-1));
        session.SealPayload([8]);Assert.True(session.SealAssistantContent([9],now));
        var repository=new RetentionRepository(transaction,session);
        await using var provider=new ServiceCollection().AddSingleton<IWhatsAppMessageRetentionRepository>(repository)
            .AddSingleton<IWhatsAppAssistantContentRetentionService>(new WhatsAppAssistantContentRetentionService(repository,transaction,new FakeTimeProvider(now),
                NullLogger<WhatsAppAssistantContentRetentionService>.Instance)).BuildServiceProvider();
        using var service=new WhatsAppMessageRetentionService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WhatsAppMessageRetentionOptions{MessageRetentionDays=90,ApplyMessageRetentionInBackground=false}),new FakeTimeProvider(now),
            NullLogger<WhatsAppMessageRetentionService>.Instance);
        Assert.Equal(1,await service.ClearExpiredTextsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(session.ProtectedAssistantContent);Assert.Equal(new byte[]{8},session.ProtectedPayload);
        Assert.Equal(1,transaction.Count);Assert.Equal(1,repository.Bodies);Assert.True(repository.Limit>0);
    }
    private sealed class RetentionRepository(Transaction transaction,WhatsAppFlowSession session) : IWhatsAppMessageRetentionRepository
    {
        public int Bodies {get;private set;}public int Limit {get;private set;}
        public Task<int> ClearExpiredBodiesAsync(DateTime cutoffUtc,CancellationToken ct)
        {Assert.False(transaction.IsOpen);Bodies++;return Task.FromResult(1);}
        public Task<int> ClearExpiredAssistantContentAsync(DateTime nowUtc,int limit,CancellationToken ct)
        {Assert.True(transaction.IsOpen);Limit=limit;return Task.FromResult(session.ClearAssistantContent(nowUtc)?1:0);}
    }
    private sealed class Transaction : IUnitOfWork
    {
        public bool IsOpen {get;private set;}public int Count {get;private set;}
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken,Task<T>> work,CommitPolicy policy,CancellationToken ct) where T:Result
        {
            Assert.False(IsOpen);Assert.Equal(CommitPolicy.OnSuccess,policy);IsOpen=true;Count++;
            try{return await work(ct);}finally{IsOpen=false;}
        }
    }
}
