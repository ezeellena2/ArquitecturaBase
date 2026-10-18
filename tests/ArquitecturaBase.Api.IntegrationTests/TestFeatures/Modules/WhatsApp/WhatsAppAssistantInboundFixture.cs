using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppAssistantInboundFixture : IAsyncDisposable
{
    public Guid ContactId {get;}=Guid.NewGuid();
    public List<string> Events {get;}=[];
    public List<Guid> Scopes {get;}=[];
    public bool HasWork {get;set;}=true;
    public bool ContactTaken {get;set;}
    public bool ReuseExistingOperation {get;set;}
    public bool ConfiguredWork {get;set;}
    public bool HasAudioWork {get;set;}
    public bool AudioOnlyContact {get;set;}
    public bool AudioReady {get;set;}
    public bool InterpretAfterAudio {get;set;}
    public bool RejectAudioSubmit {get;set;}
    public Result<AssistantOperationResponse>? AudioCompleted {get;private set;}
    public WhatsAppAssistantWorkResponse AudioWork {get;}
    public bool RejectSubmit {get;set;}
    public string? ExistingAssistantState {get;set;}
    public Error? AssistantReadFailure {get;set;}
    public Result<AssistantOperationResponse>? Completed {get;private set;}
    public WhatsAppAssistantWorkResponse Work {get;}
    private readonly ServiceProvider _provider;
    public WhatsAppInboundProcessor Processor {get;}
    public WhatsAppAssistantInboundFixture()
    {
        var now=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);
        var context=new AssistantChannelContext("whatsapp-manual",Guid.NewGuid(),Guid.NewGuid(),"es","America/Argentina/Buenos_Aires",new([],[]),[],[new("assistant_proposal",1)]);
        Work=new(Guid.NewGuid(),new(Guid.NewGuid(),context,"Fictitious visit",now,now.AddDays(90)));
        Work=Work with {Operation=Work.Operation! with {RequestId=Work.RequestId}};
        AudioWork=new(Guid.NewGuid(),new(Guid.NewGuid(),context with {RequiredPermissions=new([WhatsAppPermissions.Read],[])},"",now,now.AddDays(90))
            {Audio=new("private/fictitious-audio","voice.ogg","audio/ogg",100,new string('a',64),now.AddDays(90))});
        AudioWork=AudioWork with {Operation=AudioWork.Operation! with {RequestId=AudioWork.RequestId}};
        var services=new ServiceCollection().AddScoped(_=>new ScopeIdentity())
            .AddSingleton<IWhatsAppMessageRepository>(new Messages())
            .AddSingleton<IWhatsAppAssistantReader>(new Reader(this))
            .AddSingleton<IWhatsAppAudioReader>(new AudioReader(this))
            .AddScoped<IWhatsAppAudioDispatchService>(sp=>new AudioDispatch(this,sp.GetRequiredService<ScopeIdentity>().Id))
            .AddScoped<IWhatsAppInboundService>(sp=>new Inbound(this,sp.GetRequiredService<ScopeIdentity>().Id))
            .AddScoped<IWhatsAppAssistantDispatchService>(sp=>new Dispatch(this,sp.GetRequiredService<ScopeIdentity>().Id))
            .AddScoped<IAssistantOperations>(sp=>new Operations(this,sp.GetRequiredService<ScopeIdentity>().Id));
        _provider=services.BuildServiceProvider();
        Processor=new(new WhatsAppInboundSignal(),_provider.GetRequiredService<IServiceScopeFactory>(),Options.Create(new WhatsAppOptions{ProcessInboundInBackground=false}),
            new FakeTimeProvider(now),NullLogger<WhatsAppInboundProcessor>.Instance);
    }
    public ValueTask DisposeAsync(){Processor.Dispose();return _provider.DisposeAsync();}
    private sealed class ScopeIdentity {public Guid Id {get;}=Guid.NewGuid();}
    private sealed class Inbound(WhatsAppAssistantInboundFixture host,Guid scope) : IWhatsAppInboundService
    {
        public Task<Result<bool>> ProcessContactAsync(Guid id,CancellationToken ct)
        {host.Events.Add("access");host.Scopes.Add(scope);return Task.FromResult(Result.Success(!host.ContactTaken));}
    }
    private sealed class Dispatch(WhatsAppAssistantInboundFixture host,Guid scope) : IWhatsAppAssistantDispatchService
    {
        public Task<Result<WhatsAppAssistantWorkResponse?>> PrepareAsync(Guid id,CancellationToken ct)
        {host.Events.Add("prepare");host.Scopes.Add(scope);return Task.FromResult(Result.Success<WhatsAppAssistantWorkResponse?>(host.HasWork?host.Work with {Operation=host.ConfiguredWork?null:host.Work.Operation,ShouldSubmit=!host.ConfiguredWork&&!host.ReuseExistingOperation}:null));}
        public Task<Result> CompleteConfiguredAsync(Guid contactId,Guid requestId,CancellationToken ct)
        {Assert.Equal(host.ContactId,contactId);Assert.Equal(host.Work.RequestId,requestId);host.Events.Add("configured-complete");host.Scopes.Add(scope);return Task.FromResult(Result.Success());}
        public Task<Result> CompleteAsync(Guid id,Guid requestId,Result<AssistantOperationResponse> outcome,CancellationToken ct)
        {Assert.Equal(host.Work.RequestId,requestId);host.Events.Add("complete");host.Scopes.Add(scope);host.Completed=outcome;return Task.FromResult(Result.Success());}
    }
    private sealed class Operations(WhatsAppAssistantInboundFixture host,Guid scope) : IAssistantOperations
    {
        public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request,CancellationToken ct)
        {
            if(request.RequestId==host.AudioWork.RequestId)
            {
                Assert.Equal(host.AudioWork.Operation,request);host.Events.Add("submit-audio");host.Scopes.Add(scope);
                return Task.FromResult(host.RejectAudioSubmit?Result.Failure<AssistantOperationAcceptedResponse>(WhatsAppAssistantErrors.Unavailable):Result.Success(new AssistantOperationAcceptedResponse(request.RequestId,"pending")));
            }
            Assert.Equal(host.Work.Operation,request);host.Events.Add("submit");host.Scopes.Add(scope);
            if(!host.RejectSubmit)host.ExistingAssistantState="pending";
            return Task.FromResult(host.RejectSubmit?Result.Failure<AssistantOperationAcceptedResponse>(WhatsAppAssistantErrors.Unavailable):Result.Success(new AssistantOperationAcceptedResponse(request.RequestId,"pending")));
        }
        public Task<Result> CancelAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context,Guid id,CancellationToken ct)
        {
            if(id==host.AudioWork.RequestId)
            {
                Assert.Equal(host.AudioWork.Operation!.Context,context);host.Events.Add("get-audio");host.Scopes.Add(scope);
                return Task.FromResult(Result.Success(new AssistantOperationResponse(id,host.AudioReady?"completed":"pending",null,null,null)
                    {NormalizedAudio=host.AudioReady?new("Fictitious normalized visit",["es"]):null}));
            }
            Assert.Equal(host.Work.Operation!.Context,context);host.Events.Add("get");host.Scopes.Add(scope);
            if(host.AssistantReadFailure is {} error)return Task.FromResult(Result.Failure<AssistantOperationResponse>(error));
            return Task.FromResult(host.ExistingAssistantState is not null || host.ReuseExistingOperation
                ? Result.Success(new AssistantOperationResponse(id,host.ExistingAssistantState??"pending",null,null,null))
                : Result.Failure<AssistantOperationResponse>(WhatsAppChatErrors.NotFound));
        }
    }
    private sealed class Reader(WhatsAppAssistantInboundFixture host) : IWhatsAppAssistantReader
    {
        public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Guid>>(excluded.Contains(host.ContactId) || host.AudioOnlyContact?[]:[host.ContactId]);
        public Task<WhatsAppAssistantReferenceRow?> FindByOutgoingAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAssistantSuggestionPointerResponse?> FindCurrentAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessageRow?> FindOutgoingAsync(Guid id,Guid requestId,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAssistantReferenceRow?> FindAsync(Guid id,Guid requestId,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAssistantSourceRow?> FindSourceAsync(Guid id,Guid source,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAssistantChoiceRow?> FindLatestChoiceAsync(Guid contactId,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAssistantSourceRow?> FindLatestSourceAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<int> CountOffersAsync(Guid id,DateTime from,DateTime to,CancellationToken ct,Guid? excludingRequestId=null)=>throw new NotSupportedException();
        public Task<bool> ExistsCurrentContextAsync(WhatsAppAssistantReferenceRow reference,DateTime nowUtc,CancellationToken ct)=>Task.FromResult(true);
        public Task<bool> ExistsUnresolvedOfferAsync(Guid contactId,Guid excludingRequestId,CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> ExistsDeclinedAsync(Guid id,Guid definition,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<StructuredFormOfferReference>> ListOffersAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListDeclinedDefinitionIdsAsync(Guid id,IReadOnlyCollection<Guid> definitionIds,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class AudioDispatch(WhatsAppAssistantInboundFixture host,Guid scope) : IWhatsAppAudioDispatchService
    {
        public Task<Result<WhatsAppAssistantWorkResponse?>> PrepareAsync(Guid id,CancellationToken ct)
        {
            if(host.HasAudioWork){host.Events.Add("audio-prepare");host.Scopes.Add(scope);}
            return Task.FromResult(Result.Success<WhatsAppAssistantWorkResponse?>(host.HasAudioWork?host.AudioWork:null));
        }
        public Task<Result> CompleteAsync(Guid id,Guid requestId,Result<AssistantOperationResponse> outcome,CancellationToken ct)
        {
            Assert.Equal(host.AudioWork.RequestId,requestId);host.Events.Add("audio-complete");host.Scopes.Add(scope);host.AudioCompleted=outcome;
            if(host.InterpretAfterAudio && outcome.IsSuccess && outcome.Value.NormalizedAudio is not null)host.HasWork=true;
            return Task.FromResult(Result.Success());
        }
    }
    private sealed class AudioReader(WhatsAppAssistantInboundFixture host) : IWhatsAppAudioReader
    {
        public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct)=>
            Task.FromResult<IReadOnlyList<Guid>>(host.HasAudioWork && !excluded.Contains(host.ContactId)?[host.ContactId]:[]);
        public Task<WhatsAppAudioSourceRow?> FindPendingAsync(Guid id,DateTime nowUtc,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAudioSourceRow?> FindAsync(Guid id,Guid message,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> keys,DateTime cutoffUtc,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Messages : IWhatsAppMessageRepository
    {
        public Task<IReadOnlyList<Guid>> ListContactsWithPendingInboundAsync(IReadOnlyCollection<Guid> excluded,int limit,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task LockAsync(IReadOnlyCollection<string> ids,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlySet<string>> ListExistingIdsAsync(IReadOnlyCollection<string> ids,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppMessage>> ListOutboundAsync(IReadOnlyCollection<string> ids,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppMessage>> ListPendingInboundAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppMessage?> GetInboundAsync(string id,CancellationToken ct)=>throw new NotSupportedException();
        public void Add(WhatsAppMessage row)=>throw new NotSupportedException();
        public Task<WhatsAppDeliveryReceipt?> GetReceiptAsync(string id,CancellationToken ct)=>throw new NotSupportedException();
        public void Add(WhatsAppDeliveryReceipt row)=>throw new NotSupportedException();
    }
}
