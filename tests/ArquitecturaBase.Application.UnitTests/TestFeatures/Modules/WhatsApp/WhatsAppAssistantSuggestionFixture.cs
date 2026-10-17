using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppAssistantSuggestionFixture
{
    public WhatsAppAssistantContextFixture Context {get;}=new();
    public FakeAssistantForms Forms {get;}
    public SessionRepository Sessions {get;}=new();
    public SessionProtector Protector {get;}=new();
    public FakeWhatsAppAssistantReader Reader {get;}=new();
    public Repository Requests {get;}=new();
    public Conversations Conversation {get;}
    public FakeUnitOfWork UnitOfWork {get;}
    public Operations Assistant {get;}
    public Signal Wake {get;}
    public WhatsAppAssistantService Service {get;}
    public Guid SourceId {get;}=Guid.NewGuid();
    public RequestWhatsAppAssistantSuggestionRequest Request {get;}
    public WhatsAppAssistantSuggestionFixture(FakeAssistantForms? forms = null)
    {
        Forms = forms ?? new FakeAssistantForms();
        Conversation=new(Context.Contact);
        Reader.Source=new(SourceId,Context.Contact.Id,"Fictitious visit tomorrow",Context.Clock.GetUtcNow().UtcDateTime,Context.Clock.GetUtcNow().UtcDateTime.AddDays(90));
        UnitOfWork=new(){OnCommit=()=>
        {
            Reader.Reference=Requests.Rows.LastOrDefault() is {} row?WhatsAppAssistantSuggestionPolicy.Reference(row):null;
            if(Conversation.Outgoing.LastOrDefault() is {} outgoing && Sessions.Rows.SingleOrDefault(session=>session.Id==outgoing.FlowSessionId) is {} session)
                Reader.Outgoing=new(outgoing.Id,outgoing.RequestId,outgoing.OperatorUserId,outgoing.Body,outgoing.RequestedAtUtc,outgoing.State,outgoing.WaMessageId,
                    outgoing.Kind,new(session.DefinitionId,session.VersionId,session.VersionNumber,session.NameJson,session.Culture),outgoing.Origin);
        }};
        Assistant=new(UnitOfWork); Wake=new(UnitOfWork);
        Service=new(Context.Policy,new(Reader,Assistant,Forms,Context.Clock),Requests,Conversation,UnitOfWork,Wake,WhatsAppAssistantAcceptanceFixture.Create(this),NullLogger<WhatsAppAssistantService>.Instance);
        Request=new(Context.Contact.Id,Guid.NewGuid(),SourceId,Context.Contact.ModeRevision);
    }
    public static async Task<WhatsAppAssistantSuggestionFixture> ReadyAsync(string recipientCulture = "es")
    {
        var host=new WhatsAppAssistantSuggestionFixture();
        if (recipientCulture != "es") await host.Context.Users.UpdateProfileAsync(host.Context.Subject.Id,"Fictitious subject",recipientCulture,"America/Argentina/Buenos_Aires",TestContext.Current.CancellationToken);
        Assert.True((await host.Service.RequestAsync(host.Request,TestContext.Current.CancellationToken)).IsSuccess);
        var first=Guid.NewGuid();var second=Guid.NewGuid();
        host.Forms.Prefill=new StructuredFormPrefillResponse(new(Guid.NewGuid(),Guid.NewGuid(),2,"VIS",new Dictionary<string,string>{{"es","Visita"}},2,
            new Dictionary<string,string>{{"es","Guardar"}}),4,
            [new(first,"field_"+first.ToString("N"),"shortText","Detalle","valor privado","valor privado"),
             new(second,"field_"+second.ToString("N"),"boolean","Confirmado","false","No")],[],"Contó una visita.","es");
        var proposal=new AssistantObservedProposal(Guid.NewGuid(),Guid.NewGuid(),new("forms.propose-registration",1),new WhatsAppAssistantNoReplyPayload("not_form"));
        host.Assistant.Outcome=new AssistantOperationResponse(host.Request.RequestId,"completed",proposal,
            new("qualified-text",1,"neutral",1,host.Context.Clock.GetUtcNow().UtcDateTime,"whatsapp-manual"),null);
        return host;
    }
    public sealed class SessionRepository : IWhatsAppFlowSessionRepository
    {
        public List<WhatsAppFlowSession> Rows {get;}=[];
        public Task<WhatsAppFlowSession?> GetAsync(Guid id,CancellationToken ct)=>Task.FromResult(Rows.SingleOrDefault(row=>row.Id==id));
        public void Add(WhatsAppFlowSession row)=>Rows.Add(row);
    }
    public sealed class SessionProtector : IWhatsAppFlowSessionProtector
    {
        public WhatsAppFlowSessionPayload? Payload {get;set;}
        public WhatsAppFlowAssistantContent? Content {get;set;}
        public byte[] Protect(Guid sessionId,Guid contactId,Guid userId,Guid versionId,WhatsAppFlowSessionPayload payload) {Payload=payload;return [1];}
        public WhatsAppFlowSessionPayload Read(Guid sessionId,Guid contactId,Guid userId,Guid versionId,byte[] payload)=>Payload!;
        public byte[] ProtectAssistant(Guid sessionId,Guid contactId,Guid userId,Guid versionId,WhatsAppFlowAssistantContent content) {Content=content;return [2];}
        public WhatsAppFlowAssistantContent ReadAssistant(Guid sessionId,Guid contactId,Guid userId,Guid versionId,byte[] content)=>Content!;
    }
    public sealed class Repository : IWhatsAppAssistantRepository
    {
        public List<WhatsAppAssistantRequest> Rows {get;}=[];
        public Task<WhatsAppAssistantRequest?> GetByRequestAsync(Guid id,CancellationToken ct)=>Task.FromResult(Rows.SingleOrDefault(row=>row.RequestId==id));
        public Task<WhatsAppAssistantRequest?> GetBySourceAsync(Guid contactId,Guid sourceId,WhatsAppAssistantRequestKind kind,CancellationToken ct)=>Task.FromResult(Rows.SingleOrDefault(row=>row.ContactId==contactId && row.SourceMessageId==sourceId && row.Kind==kind));
        public Task<WhatsAppAssistantRequest?> GetPendingAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(Rows.FirstOrDefault(row=>row.ContactId==contactId && row.State==WhatsAppAssistantRequestState.Pending));
        public void Add(WhatsAppAssistantRequest row)=>Rows.Add(row);
    }
    public sealed class Operations(FakeUnitOfWork transaction) : IAssistantOperations
    {
        public string Availability {get;set;}="available";
        public int Submits {get;private set;}
        public int Gets {get;private set;}
        public Result<AssistantOperationResponse> Outcome {get;set;}=WhatsAppChatErrors.NotFound;
        public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken ct)=>Task.FromResult(new AssistantAvailabilityResponse(Availability));
        public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request,CancellationToken ct)
        { Assert.False(transaction.InTransaction);Submits++;return Task.FromResult(Result.Success(new AssistantOperationAcceptedResponse(request.RequestId,"pending"))); }
        public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context,Guid id,CancellationToken ct)
        { Assert.False(transaction.InTransaction);Gets++;return Task.FromResult(Outcome); }
    }
    public sealed class Signal(FakeUnitOfWork transaction) : IWhatsAppInboundSignal
    {
        public int Notifications {get;private set;}
        public void Notify(){Assert.False(transaction.InTransaction);Notifications++;}
    }
    public sealed class Conversations(WhatsAppContact contact) : IWhatsAppConversationRepository
    {
        public List<WhatsAppOutgoingMessage> Outgoing {get;}=[];
        public Task<WhatsAppContact?> GetForUpdateAsync(Guid id,CancellationToken ct)=>Task.FromResult<WhatsAppContact?>(id==contact.Id?contact:null);
        public Task<WhatsAppReadState?> GetReadStateAsync(Guid id,Guid actor,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid id,Guid actor,Guid request,CancellationToken ct)=>Task.FromResult(Outgoing.SingleOrDefault(row=>row.ContactId==id && row.OperatorUserId==actor && row.RequestId==request));
        public Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid id,CancellationToken ct)=>Task.FromResult(Outgoing.SingleOrDefault(row=>row.Id==id));
        public Task<bool> ExistsUnresolvedOutgoingAsync(Guid id,CancellationToken ct)=>Task.FromResult(false);
        public Task<bool> ExistsStartedAssistantMessageAsync(Guid id,CancellationToken ct)=>Task.FromResult(false);
        public Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoff,CancellationToken ct)=>throw new NotSupportedException();
        public void Add(WhatsAppReadState row)=>throw new NotSupportedException();
        public void Add(WhatsAppOutgoingMessage row)=>Outgoing.Add(row);
    }
}
