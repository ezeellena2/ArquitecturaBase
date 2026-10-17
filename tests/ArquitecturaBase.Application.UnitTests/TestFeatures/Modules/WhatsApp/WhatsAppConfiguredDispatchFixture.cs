using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppConfiguredDispatchFixture
{
    public required WhatsAppAssistantSuggestionFixture Host {get;init;}
    public required Guid RecordId {get;init;}
    public required Guid SetId {get;init;}
    public required Guid SuggestionId {get;init;}
    public required WhatsAppMessage Source {get;set;}
    public required WhatsAppAssistantDispatchService Service {get;init;}
    public static async Task<WhatsAppConfiguredDispatchFixture> CreateAsync(bool automatic)
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();host.Requests.Rows.Clear();host.Reader.Reference=null;host.Assistant.ResetCalls();host.Assistant.Availability="paused";
        if(automatic)host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var record=Guid.NewGuid();var set=Guid.NewGuid();var suggestion=Guid.NewGuid();
        var source=Capture(host,record,1);
        var prefill=host.Forms.Prefill.Value;
        host.Forms.Suggestion=new StructuredFormSuggestionPreparationResponse(prefill,prefill.Fields,new(record,suggestion,set,[]),false);
        host.Forms.Suggestions=new StructuredFormSuggestionsResponse(record,set,1,"Visita","VIS-000001",[],
            [new(suggestion,"Always",null,prefill.Form,prefill.DefinitionRevision,"es",[],[],prefill.Fields,false)]);
        var service=new WhatsAppAssistantDispatchService(host.Context.Policy,new(host.Reader,host.Assistant,host.Forms,host.Context.Clock,host.Presentation),
            host.Reader,host.Requests,host.Conversation,host.UnitOfWork,WhatsAppAssistantAcceptanceFixture.CreateAutomatic(host),NullLogger<WhatsAppAssistantDispatchService>.Instance);
        return new(){Host=host,RecordId=record,SetId=set,SuggestionId=suggestion,Source=source,Service=service};
    }
    public static WhatsAppMessage Capture(WhatsAppAssistantSuggestionFixture host,Guid recordId,long sequence)
    {
        var now=host.Context.Clock.GetUtcNow().UtcDateTime;
        var message=WhatsAppMessage.Inbound(host.Context.Contact.Id,"wamid.capture-"+Guid.NewGuid().ToString("N"),WhatsAppMessageKind.FlowReply,null,null,now,protectedFlowResponse:[1]);
        message.Classify(WhatsAppMessagePurpose.Business);message.SetFlowReplyCapture(WhatsAppFlowCaptureState.Completed,recordId,"VIS-000001",null);
        message.MarkProcessed(now);message.SetInboundSequence(sequence);host.Messages.Messages.Add(message);
        host.Reader.Source=new(message.Id,host.Context.Contact.Id,"",now,now.AddDays(90)){CapturedRecordId=recordId,WaMessageId=message.WaMessageId};
        return message;
    }
    public async Task<WhatsAppAssistantWorkResponse> PrepareAsync()
    {
        var prepared=await Service.PrepareAsync(Host.Context.Contact.Id,TestContext.Current.CancellationToken);
        Assert.True(prepared.IsSuccess);return Assert.IsType<WhatsAppAssistantWorkResponse>(prepared.Value);
    }
    public Task CompleteAsync(Guid id)=>Service.CompleteConfiguredAsync(Host.Context.Contact.Id,id,TestContext.Current.CancellationToken);
}
