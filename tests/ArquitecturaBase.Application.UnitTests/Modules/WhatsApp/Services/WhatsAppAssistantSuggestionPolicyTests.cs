using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAssistantSuggestionPolicyTests
{
    private static async Task<(WhatsAppAssistantSuggestionFixture Host,WhatsAppAssistantReferenceRow Reference)> ReferenceAsync()
    {
        var host=new WhatsAppAssistantSuggestionFixture();
        var captured=await host.Context.Policy.CaptureAsync(host.Context.Contact,host.Context.Operator.Id,TestContext.Current.CancellationToken);
        Assert.True(captured.IsSuccess);
        var row=WhatsAppAssistantRequest.Create(host.Request.RequestId,host.Context.Contact.Id,host.SourceId,host.Context.Operator.Id,host.Context.Subject.Id,
            WhatsAppAssistantRequestKind.Manual,host.Context.Contact.ModeRevision,host.Context.Contact.LinkRevision,JsonSerializer.Serialize(captured.Value.Context,JsonSerializerOptions.Web),
            host.Context.Clock.GetUtcNow().UtcDateTime,host.Context.Clock.GetUtcNow().UtcDateTime.AddDays(90));
        host.Requests.Add(row);host.Reader.Reference=WhatsAppAssistantSuggestionPolicy.Reference(row);
        return(host,host.Reader.Reference!);
    }
    private static AssistantObservedProposal Proposal(string id)=>new(Guid.NewGuid(),Guid.NewGuid(),new(id,1),new WhatsAppAssistantNoReplyPayload("not_form"));
    private static AssistantOperationResponse Completed(Guid requestId,AssistantObservedProposal? proposal)=>new(requestId,"completed",proposal,new("qualified-text",1,"neutral",1,WhatsAppAssistantContextFixture.Now.UtcDateTime,"whatsapp-manual"),null);

    [Fact]
    public async Task A_ready_preview_is_validated_by_the_form_owner_without_persisting_model_payload_or_canonical_values_in_http()
    {
        var(host,reference)=await ReferenceAsync();var forms=new FakeAssistantForms();var proposal=Proposal("forms.propose-registration");
        var canonical=Guid.NewGuid().ToString("D");var field=Guid.NewGuid();
        forms.Prefill=new StructuredFormPrefillResponse(new(Guid.NewGuid(),Guid.NewGuid(),2,"V",new Dictionary<string,string>{{"es","Visita"}},1,new Dictionary<string,string>{{"es","Enviar"}}),3,
            [new(field,"field_"+field.ToString("N"),"choice","Persona",canonical,"Persona ficticia")],[],"Contó una visita.","es");
        host.Assistant.Outcome=Completed(reference.RequestId,proposal);
        var result=await new WhatsAppAssistantSuggestionPolicy(host.Reader,host.Assistant,forms,host.Context.Clock).ReadAsync(reference,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Ready,result.Value.Response.State);
        Assert.Same(proposal,forms.LastPrefill!.Proposal);Assert.Equal(reference.SubjectUserId,forms.LastPrefill.RecipientUserId);Assert.False(forms.LastPrefill.ForSending);
        var response=result.Value.Response.Proposal!;Assert.Equal("Visita",response.Name);Assert.Equal("es",response.Culture);Assert.Equal(field,Assert.Single(response.Fields).FieldId);
        var json=JsonSerializer.Serialize(result.Value.Response,JsonSerializerOptions.Web);
        Assert.DoesNotContain(canonical,json,StringComparison.Ordinal);Assert.DoesNotContain("observedReads",json,StringComparison.Ordinal);Assert.DoesNotContain("qualified-text",json,StringComparison.Ordinal);
        Assert.Equal(WhatsAppAssistantRequestState.Pending,host.Requests.Rows[0].State);Assert.Equal(0,host.UnitOfWork.Transactions);
    }
    [Fact]
    public async Task An_owner_rejection_removes_the_entire_preview_without_using_model_ids_as_authority()
    {
        var(host,reference)=await ReferenceAsync();var forms=new FakeAssistantForms();host.Assistant.Outcome=Completed(reference.RequestId,Proposal("forms.propose-registration"));
        var result=await new WhatsAppAssistantSuggestionPolicy(host.Reader,host.Assistant,forms,host.Context.Clock).ReadAsync(reference,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Blocked,result.Value.Response.State);Assert.Null(result.Value.Response.Proposal);Assert.Equal(1,forms.Previews);
    }
    [Fact]
    public async Task An_expired_source_is_hidden_before_consulting_the_model_receipt()
    {
        var(host,reference)=await ReferenceAsync();reference=reference with{ContentExpiresAtUtc=host.Context.Clock.GetUtcNow().UtcDateTime};
        var result=await new WhatsAppAssistantSuggestionPolicy(host.Reader,host.Assistant,new FakeAssistantForms(),host.Context.Clock).ReadAsync(reference,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Blocked,result.Value.Response.State);Assert.Equal(0,host.Assistant.Gets);
    }
    [Fact]
    public async Task A_shared_sent_receipt_survives_model_expiry_and_preserves_the_original_operator_and_request_id()
    {
        var(host,reference)=await ReferenceAsync();var outgoingId=Guid.NewGuid();var sendId=Guid.NewGuid();var original=host.Context.Operator.Id;
        reference=reference with{State=WhatsAppAssistantRequestState.Sent,OutgoingId=outgoingId};
        host.Reader.Outgoing=new(outgoingId,sendId,original,"Completá el formulario.",host.Context.Clock.GetUtcNow().UtcDateTime,WhatsAppOutgoingMessageState.Accepted,"wamid.fixture",WhatsAppOutgoingMessageKind.Flow);
        var result=await new WhatsAppAssistantSuggestionPolicy(host.Reader,host.Assistant,new FakeAssistantForms(),host.Context.Clock).ReadAsync(reference,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Null(result.Value.Response.Proposal);Assert.NotNull(result.Value.Response.Outgoing);Assert.Equal(sendId,result.Value.Response.Outgoing.RequestId);Assert.Equal(original,result.Value.Response.Outgoing.OperatorUserId);Assert.Equal(0,host.Assistant.Gets);
    }
    [Fact]
    public async Task A_no_reply_proposal_never_becomes_a_free_message_or_a_form_preview()
    {
        var(host,reference)=await ReferenceAsync();var forms=new FakeAssistantForms();host.Assistant.Outcome=Completed(reference.RequestId,Proposal("whatsapp.no-reply"));
        var result=await new WhatsAppAssistantSuggestionPolicy(host.Reader,host.Assistant,forms,host.Context.Clock).ReadAsync(reference,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.NoReply,result.Value.Response.State);Assert.Null(result.Value.Response.Outgoing);Assert.Equal(0,forms.Previews);
    }
    [Fact]
    public async Task An_uncertain_model_turn_is_not_resubmitted_or_treated_as_a_missing_form()
    {
        var(host,reference)=await ReferenceAsync();host.Assistant.Outcome=new AssistantOperationResponse(reference.RequestId,"uncertain",null,null,"technical-unknown");
        var result=await new WhatsAppAssistantSuggestionPolicy(host.Reader,host.Assistant,new FakeAssistantForms(),host.Context.Clock).ReadAsync(reference,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Blocked,result.Value.Response.State);Assert.Null(result.Value.Response.Proposal);Assert.Equal(0,host.Assistant.Submits);
    }

    [Fact]
    public async Task A_reference_that_no_longer_belongs_to_the_current_source_or_link_cannot_reveal_a_preview()
    {
        var(host,reference)=await ReferenceAsync();host.Reader.CurrentIsVisible=false;
        var result=await new WhatsAppAssistantSuggestionPolicy(host.Reader,host.Assistant,new FakeAssistantForms(),host.Context.Clock).ReadAsync(reference,TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(WhatsAppAssistantErrors.StateConflict.Code,result.Error.Code);Assert.Equal(0,host.Assistant.Gets);
    }
}
