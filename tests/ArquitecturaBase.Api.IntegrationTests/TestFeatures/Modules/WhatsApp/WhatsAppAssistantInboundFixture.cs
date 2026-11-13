using System.Threading.Channels;
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
    public bool HasPendingAssistant {get;set;}
    public bool HasPendingAudio {get;set;}
    public DateTime? AudioRetryDueAtUtc {get;set;}
    private int _rounds;
    public int Rounds=>Volatile.Read(ref _rounds);
    private readonly PollingTimeProvider _clock;
    public FakeTimeProvider Clock=>_clock;
    public WhatsAppInboundSignal Signal {get;}=new();
    public Task<TimeSpan> WaitForPollAsync(CancellationToken ct)=>
        _clock.NextPollAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10),ct);
    public bool ContactTaken {get;set;}
    public bool ReuseExistingOperation {get;set;}
    public bool RecoverOnly {get;set;}
    public bool ConfiguredWork {get;set;}
    public bool HasAudioWork {get;set;}
    /// <summary>A received spreadsheet waits to be downloaded: its contact is pending and its step records "spreadsheet".</summary>
    public bool HasSpreadsheetWork {get;set;}
    /// <summary>A spreadsheet waits for its next download attempt: the processor polls as soon as for a pending reference.</summary>
    public bool HasPendingSpreadsheet {get;set;}
    public bool AudioOnlyContact {get;set;}
    /// <summary>Task 8 of the documents: a received document waits; its contact is pending and its step records "document-prepare".</summary>
    public bool HasDocumentWork {get;set;}
    /// <summary>A document waits for its next attempt or its reading: the processor polls as soon as for a pending reference.</summary>
    public bool HasPendingDocument {get;set;}
    /// <summary>Whether the document's step returns its reading (a PDF or a photo that is used) or keeps it without one.</summary>
    public bool DocumentReads {get;set;}=true;
    /// <summary>What the engine already has for the document's reading; null until it is submitted.</summary>
    public string? ExistingDocumentState {get;set;}
    public Result<AssistantOperationResponse>? DocumentCompleted {get;private set;}
    public WhatsAppAssistantWorkResponse DocumentWork {get;}
    public bool AudioReady {get;set;}
    public bool InterpretAfterAudio {get;set;}
    public bool RejectAudioSubmit {get;set;}
    public bool AudioRecoverOnly {get;set;}
    public string? ExistingAudioState {get;set;}
    public Result<AssistantOperationResponse>? AudioCompleted {get;private set;}
    public WhatsAppAssistantWorkResponse AudioWork {get;}
    public bool RejectSubmit {get;set;}
    public string? ExistingAssistantState {get;set;}
    public Error? AssistantReadFailure {get;set;}
    public Result<AssistantOperationResponse>? Completed {get;private set;}
    public WhatsAppAssistantWorkResponse Work {get;}
    private readonly ServiceProvider _provider;
    public WhatsAppInboundProcessor Processor {get;}
    public WhatsAppAssistantInboundFixture(bool processInBackground=false,int inboundPollSeconds=30)
    {
        var now=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);
        _clock=new(now);
        var context=new AssistantChannelContext("whatsapp-manual",Guid.NewGuid(),Guid.NewGuid(),"es","America/Argentina/Buenos_Aires",new([],[]),[],[new("assistant_proposal",1)]) { InteractionMode = AssistantInteractionMode.Manual };
        Work=new(Guid.NewGuid(),new(Guid.NewGuid(),context,"Fictitious visit",now,now.AddDays(90)));
        Work=Work with {Operation=Work.Operation! with {RequestId=Work.RequestId}};
        AudioWork=new(Guid.NewGuid(),new(Guid.NewGuid(),context with {RequiredPermissions=new([WhatsAppPermissions.Read],[])},"",now,now.AddDays(90))
            {Audio=new("private/fictitious-audio","voice.ogg","audio/ogg",100,new string('a',64),now.AddDays(90))});
        AudioWork=AudioWork with {Operation=AudioWork.Operation! with {RequestId=AudioWork.RequestId}};
        var documentContext=new AssistantChannelContext("whatsapp-assistant",context.SubjectUserId!.Value,context.SubjectUserId,"es","America/Argentina/Buenos_Aires",new([],[]),[],[]) { InteractionMode = AssistantInteractionMode.Assistant };
        DocumentWork=new(Guid.NewGuid(),new(Guid.NewGuid(),documentContext,"",now,now.AddDays(90))
            {Document=new("whatsapp/documents/fictitious.pdf","comprobante.pdf","application/pdf",100,new string('b',64),1,now.AddDays(90))});
        DocumentWork=DocumentWork with {Operation=DocumentWork.Operation! with {RequestId=DocumentWork.RequestId}};
        var services=new ServiceCollection().AddScoped(_=>new ScopeIdentity())
            .AddSingleton<IWhatsAppMessageRepository>(new Messages())
            .AddSingleton<IWhatsAppAssistantReader>(new Reader(this))
            .AddSingleton<IWhatsAppAudioReader>(new AudioReader(this))
            .AddScoped<IWhatsAppAudioDispatchService>(sp=>new AudioDispatch(this,sp.GetRequiredService<ScopeIdentity>().Id))
            .AddSingleton<IWhatsAppSpreadsheetReader>(new SpreadsheetReader(this))
            .AddScoped<IWhatsAppSpreadsheetDispatchService>(sp=>new SpreadsheetDispatch(this,sp.GetRequiredService<ScopeIdentity>().Id))
            .AddSingleton<IWhatsAppReceivedDocumentReader>(new DocumentReader(this))
            .AddScoped<IWhatsAppReceivedDocumentDispatchService>(sp=>new DocumentDispatch(this,sp.GetRequiredService<ScopeIdentity>().Id))
            .AddScoped<IWhatsAppInboundService>(sp=>new Inbound(this,sp.GetRequiredService<ScopeIdentity>().Id))
            .AddScoped<IWhatsAppAssistantDispatchService>(sp=>new Dispatch(this,sp.GetRequiredService<ScopeIdentity>().Id))
            .AddScoped<IAssistantOperations>(sp=>new Operations(this,sp.GetRequiredService<ScopeIdentity>().Id))
            .AddScoped<IWhatsAppAssistantAuthoringService>(_=>new Taps())
            .AddScoped<IWhatsAppAssistantDocumentService>(_=>new DocumentTaps())
            .AddSingleton<IWhatsAppFormActionReader>(new ActionReplies())
            .AddScoped<IWhatsAppFormActionReplyService>(_=>new ActionReplies())
            // W1-A: the processor also asks which contacts have a location to resolve; none here.
            .AddSingleton<IWhatsAppFlowSessionReader>(new Locations())
            .AddScoped<IWhatsAppFlowLocationService>(_=>new Locations());
        _provider=services.BuildServiceProvider();
        Processor=new(Signal,_provider.GetRequiredService<IServiceScopeFactory>(),Options.Create(new WhatsAppOptions{
            ProcessInboundInBackground=processInBackground,InboundPollSeconds=inboundPollSeconds}),
            _clock,NullLogger<WhatsAppInboundProcessor>.Instance);
    }
    public async ValueTask DisposeAsync()
    {
        await Processor.StopAsync(CancellationToken.None);
        Processor.Dispose();
        await _provider.DisposeAsync();
    }
    private sealed class PollingTimeProvider(DateTime nowUtc):FakeTimeProvider(new DateTimeOffset(nowUtc))
    {
        private readonly Channel<TimeSpan> _waits=Channel.CreateUnbounded<TimeSpan>();
        public ValueTask<TimeSpan> NextPollAsync(CancellationToken ct)=>_waits.Reader.ReadAsync(ct);
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {
            var timer=base.CreateTimer(callback,state,dueTime,period);
            _waits.Writer.TryWrite(dueTime);
            return timer;
        }
    }
    private sealed class ScopeIdentity {public Guid Id {get;}=Guid.NewGuid();}
    private sealed class Inbound(WhatsAppAssistantInboundFixture host,Guid scope) : IWhatsAppInboundService
    {
        public Task<Result<bool>> ProcessContactAsync(Guid id,CancellationToken ct)
        {host.Events.Add("access");host.Scopes.Add(scope);return Task.FromResult(Result.Success(!host.ContactTaken));}
    }
    // No tap on an authoring summary reaches these scenarios; the authoring flow has its own tests.
    private sealed class Taps : IWhatsAppAssistantAuthoringService
    {
        public Task<Result> ProcessTapAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(Result.Success());
    }
    // Nor a tap on a document question (task 9 of the documents): the circuit has its own tests.
    private sealed class DocumentTaps : IWhatsAppAssistantDocumentService
    {
        public Task<Result> ProcessTapAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(Result.Success());
    }
    // Nor a tap on a form action button: those replies have their own tests (WhatsAppFormActionReplyTests).
    private sealed class Locations : IWhatsAppFlowSessionReader, IWhatsAppFlowLocationService
    {
        public Task<IReadOnlyList<Guid>> ListContactsWithPendingLocationsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken cancellationToken)=>
            Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<Result<int>> ProcessAsync(Guid contactId,CancellationToken cancellationToken)=>Task.FromResult(Result.Success(0));
        public Task<IReadOnlyList<WhatsAppPendingFlowReplyRow>> ListPendingFlowRepliesAsync(int limit,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<WhatsAppFormBindingRow?> FindBindingAsync(Guid definitionId,Guid versionId,string culture,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<WhatsAppFormDispatchRow?> FindDispatchAsync(Guid outgoingId,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<WhatsAppFlowReplySessionRow?> FindReplySessionAsync(Guid contactId,string tokenHash,CancellationToken cancellationToken)=>throw new NotSupportedException();
    }

    private sealed class ActionReplies : IWhatsAppFormActionReader, IWhatsAppFormActionReplyService
    {
        public Task<Result> ProcessAsync(Guid contactId,CancellationToken cancellationToken)=>Task.FromResult(Result.Success());
        public Task<IReadOnlyList<Guid>> ListContactsWithPendingRepliesAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken cancellationToken)=>
            Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<IReadOnlyList<WhatsAppFormActionTapRow>> ListPendingRepliesAsync(Guid contactId,DateTime nowUtc,int limit,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppFormActionReplyTargetRow>> ListReplyTargetsAsync(IReadOnlyCollection<Guid> outgoingIds,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppFormActionObservationRow>> ListObservationsAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<string?> FindLinkedWaIdAsync(Guid userId,CancellationToken cancellationToken)=>throw new NotSupportedException();
    }

    private sealed class Dispatch(WhatsAppAssistantInboundFixture host,Guid scope) : IWhatsAppAssistantDispatchService
    {
        public Task<Result<WhatsAppAssistantWorkResponse?>> PrepareAsync(Guid id,CancellationToken ct)
        {host.Events.Add("prepare");host.Scopes.Add(scope);return Task.FromResult(Result.Success<WhatsAppAssistantWorkResponse?>(host.HasWork?host.Work with {Operation=host.ConfiguredWork?null:host.Work.Operation,RecoverOnly=host.RecoverOnly,ShouldSubmit=!host.ConfiguredWork&&!host.ReuseExistingOperation}:null));}
        public Task<Result> CompleteConfiguredAsync(Guid contactId,Guid requestId,CancellationToken ct)
        {Assert.Equal(host.ContactId,contactId);Assert.Equal(host.Work.RequestId,requestId);host.Events.Add("configured-complete");host.Scopes.Add(scope);return Task.FromResult(Result.Success());}
        public Task<Result> CompleteAsync(Guid id,Guid requestId,Result<AssistantOperationResponse> outcome,CancellationToken ct)
        {
            Assert.Equal(host.Work.RequestId,requestId);host.Events.Add("complete");host.Scopes.Add(scope);host.Completed=outcome;
            if(outcome.IsSuccess && outcome.Value.State=="completed")host.HasPendingAssistant=false;
            return Task.FromResult(Result.Success());
        }
    }
    private sealed class Operations(WhatsAppAssistantInboundFixture host,Guid scope) : IAssistantOperations
    {
        public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request,CancellationToken ct)
        {
            if(request.RequestId==host.DocumentWork.RequestId)
            {
                Assert.Equal(host.DocumentWork.Operation,request);host.Events.Add("submit-document");host.Scopes.Add(scope);
                host.ExistingDocumentState ??= "pending";
                return Task.FromResult(Result.Success(new AssistantOperationAcceptedResponse(request.RequestId,"pending")));
            }
            if(request.RequestId==host.AudioWork.RequestId)
            {
                Assert.Equal(host.AudioWork.Operation,request);host.Events.Add("submit-audio");host.Scopes.Add(scope);
                if(!host.RejectAudioSubmit)host.ExistingAudioState ??= host.AudioReady ? "completed" : "pending";
                return Task.FromResult(host.RejectAudioSubmit?Result.Failure<AssistantOperationAcceptedResponse>(WhatsAppAssistantErrors.Unavailable):Result.Success(new AssistantOperationAcceptedResponse(request.RequestId,"pending")));
            }
            Assert.Equal(host.Work.Operation,request);host.Events.Add("submit");host.Scopes.Add(scope);
            if(!host.RejectSubmit)host.ExistingAssistantState="pending";
            return Task.FromResult(host.RejectSubmit?Result.Failure<AssistantOperationAcceptedResponse>(WhatsAppAssistantErrors.Unavailable):Result.Success(new AssistantOperationAcceptedResponse(request.RequestId,"pending")));
        }
        public Task<Result> CancelAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context,Guid id,CancellationToken ct)
        {
            if(id==host.DocumentWork.RequestId)
            {
                Assert.Equal(host.DocumentWork.Operation!.Context,context);host.Events.Add("get-document");host.Scopes.Add(scope);
                return Task.FromResult(host.ExistingDocumentState is {} documentState
                    ? Result.Success(new AssistantOperationResponse(id,documentState,null,null,null))
                    : Result.Failure<AssistantOperationResponse>(WhatsAppChatErrors.NotFound));
            }
            if(id==host.AudioWork.RequestId)
            {
                Assert.Equal(host.AudioWork.Operation!.Context,context);host.Events.Add("get-audio");host.Scopes.Add(scope);
                if(host.AudioRecoverOnly && host.ExistingAudioState is null)
                    return Task.FromResult(Result.Failure<AssistantOperationResponse>(WhatsAppChatErrors.NotFound));
                var state=host.ExistingAudioState ?? (host.AudioReady?"completed":"pending");
                return Task.FromResult(Result.Success(new AssistantOperationResponse(id,state,null,null,null)
                    {NormalizedAudio=state=="completed"?new("Fictitious normalized visit",["es"]):null}));
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
        public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct)
        {
            Interlocked.Increment(ref host._rounds);
            return Task.FromResult<IReadOnlyList<Guid>>(excluded.Contains(host.ContactId) || host.AudioOnlyContact?[]:[host.ContactId]);
        }
        public Task<bool> ExistsPendingAsync(DateTime nowUtc,CancellationToken ct)=>Task.FromResult(host.HasPendingAssistant);
        public Task<WhatsAppAssistantReferenceRow?> FindByOutgoingAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> ExistsCurrentLinkAsync(WhatsAppAssistantReferenceRow reference,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAssistantSuggestionPointerResponse?> FindCurrentAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessageRow?> FindOutgoingAsync(Guid id,Guid requestId,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAssistantReferenceRow?> FindAsync(Guid id,Guid requestId,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAssistantSourceRow?> FindSourceAsync(Guid id,Guid source,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAssistantChoiceRow?> FindLatestChoiceAsync(Guid contactId,CancellationToken ct)=>throw new NotSupportedException();
        // No tap on an authoring summary and no pending change: the processor goes on with the interpretation.
        public Task<WhatsAppAssistantChoiceRow?> FindLatestAuthoringTapAsync(Guid contactId,CancellationToken ct)=>Task.FromResult<WhatsAppAssistantChoiceRow?>(null);
        public Task<WhatsAppAssistantChoiceRow?> FindLatestCaseTapAsync(Guid contactId,CancellationToken ct)=>Task.FromResult<WhatsAppAssistantChoiceRow?>(null);
        public Task<Guid?> FindLatestAuthoringRequestAsync(Guid contactId,CancellationToken ct)=>Task.FromResult<Guid?>(null);
        public Task<Guid?> FindChangeRequestAsync(Guid contactId,CancellationToken ct)=>Task.FromResult<Guid?>(null);
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
            return Task.FromResult(Result.Success<WhatsAppAssistantWorkResponse?>(host.HasAudioWork?host.AudioWork with {RecoverOnly=host.AudioRecoverOnly}:null));
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
            Task.FromResult<IReadOnlyList<Guid>>(host.HasAudioWork && !excluded.Contains(host.ContactId)
                && (host.AudioRetryDueAtUtc is null || host.AudioRetryDueAtUtc<=nowUtc)?[host.ContactId]:[]);
        public Task<bool> ExistsPendingAsync(DateTime nowUtc,CancellationToken ct)=>Task.FromResult(host.HasPendingAudio);
        public Task<WhatsAppAudioSourceRow?> FindPendingAsync(Guid id,DateTime nowUtc,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppAudioSourceRow?> FindAsync(Guid id,Guid message,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> keys,DateTime cutoffUtc,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class SpreadsheetReader(WhatsAppAssistantInboundFixture host) : IWhatsAppSpreadsheetReader
    {
        public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct)=>
            Task.FromResult<IReadOnlyList<Guid>>(host.HasSpreadsheetWork && !excluded.Contains(host.ContactId)?[host.ContactId]:[]);
        public Task<bool> ExistsPendingAsync(DateTime nowUtc,CancellationToken ct)=>Task.FromResult(host.HasPendingSpreadsheet);
        public Task<WhatsAppSpreadsheetSourceRow?> FindPendingAsync(Guid id,DateTime nowUtc,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppSpreadsheetSourceRow?> FindAsync(Guid id,Guid message,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListRetainedMessageIdsAsync(IReadOnlyCollection<Guid> ids,DateTime cutoffUtc,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class SpreadsheetDispatch(WhatsAppAssistantInboundFixture host,Guid scope) : IWhatsAppSpreadsheetDispatchService
    {
        public Task<Result> PrepareAsync(Guid id,CancellationToken ct)
        {
            if(host.HasSpreadsheetWork){host.Events.Add("spreadsheet");host.Scopes.Add(scope);host.HasSpreadsheetWork=false;}
            return Task.FromResult(Result.Success());
        }
    }
    private sealed class DocumentReader(WhatsAppAssistantInboundFixture host) : IWhatsAppReceivedDocumentReader
    {
        public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct)=>
            Task.FromResult<IReadOnlyList<Guid>>(host.HasDocumentWork && !excluded.Contains(host.ContactId)?[host.ContactId]:[]);
        public Task<bool> ExistsPendingAsync(DateTime nowUtc,CancellationToken ct)=>Task.FromResult(host.HasPendingDocument);
        public Task<WhatsAppReceivedDocumentSourceRow?> FindPendingAsync(Guid id,DateTime nowUtc,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppReceivedDocumentSourceRow?> FindAsync(Guid id,Guid message,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppReceivedDocumentSourceRow?> FindByRequestAsync(Guid id,Guid request,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListRetainedMessageIdsAsync(IReadOnlyCollection<Guid> ids,DateTime cutoffUtc,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class DocumentDispatch(WhatsAppAssistantInboundFixture host,Guid scope) : IWhatsAppReceivedDocumentDispatchService
    {
        public Task<Result<WhatsAppAssistantWorkResponse?>> PrepareAsync(Guid id,CancellationToken ct)
        {
            if(!host.HasDocumentWork)return Task.FromResult(Result.Success<WhatsAppAssistantWorkResponse?>(null));
            host.Events.Add("document-prepare");host.Scopes.Add(scope);host.HasDocumentWork=false;
            return Task.FromResult(Result.Success<WhatsAppAssistantWorkResponse?>(host.DocumentReads?host.DocumentWork:null));
        }
        public Task<Result> CompleteAsync(Guid id,Guid requestId,Result<AssistantOperationResponse> outcome,CancellationToken ct)
        {
            Assert.Equal(host.DocumentWork.RequestId,requestId);host.Events.Add("document-complete");host.Scopes.Add(scope);host.DocumentCompleted=outcome;
            return Task.FromResult(Result.Success());
        }
    }
    private sealed class Messages : IWhatsAppMessageRepository
    {
        public Task<IReadOnlyList<WhatsAppMessage>> ListLocationAnswersAsync(Guid contactId, DateTime sinceUtc, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WhatsAppMessage>>([]);
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
