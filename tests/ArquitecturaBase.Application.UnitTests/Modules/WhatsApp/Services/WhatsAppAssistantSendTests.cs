using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAssistantSendTests
{
    private static SendWhatsAppAssistantSuggestionRequest Send(WhatsAppAssistantSuggestionFixture host,IReadOnlyList<Guid>? fields=null)=>
        new(host.Context.Contact.Id,host.Request.RequestId,Guid.NewGuid(),fields??[host.Forms.Prefill.Value.Fields[0].FieldId]);
    [Fact]
    public async Task Human_confirmation_freezes_only_selected_values_in_the_existing_session_and_queue()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();host.Reader.OfferCount=2;var request=Send(host);var transactions=host.UnitOfWork.Transactions;
        var result=await host.Service.SendAsync(request,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(transactions+1,host.UnitOfWork.Transactions);
        var outgoing=Assert.Single(host.Conversation.Outgoing);var session=Assert.Single(host.Sessions.Rows);
        Assert.Equal(WhatsAppMessageOrigin.Conversation,outgoing.Origin);Assert.Equal(host.Context.Operator.Id,outgoing.OperatorUserId);
        Assert.Equal(session.Id,outgoing.FlowSessionId);Assert.Equal(WhatsAppAssistantRequestState.Sent,host.Requests.Rows[0].State);
        var origin=host.Protector.Content!.Origin;Assert.Equal(StructuredRecordSenderKind.Manual,origin.SenderKind);
        Assert.Equal(host.Context.Operator.Id,origin.OperatorUserId);Assert.Equal(2,origin.ProposedValues.Count);
        Assert.Equal(request.IncludedFieldIds,origin.IncludedFieldIds);Assert.Equal("whatsapp-manual",origin.Proposal!.Provenance.ChannelId);
        Assert.Equal("es",session.Culture);Assert.Equal(2,host.Reader.OfferCount);Assert.Equal(0,host.Assistant.Submits);
    }
    [Fact]
    public async Task Two_operators_share_the_first_accepted_receipt_even_after_the_model_receipt_expires()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();var row=host.Requests.Rows[0];var proposal=host.Assistant.Outcome.Value.Proposal!;
        row.Ready(proposal.Id,host.Forms.Prefill.Value.Form.DefinitionId,host.Context.Clock.GetUtcNow().UtcDateTime);
        var originalId=Guid.NewGuid();var outgoingId=Guid.NewGuid();row.Accept(outgoingId,new string('a',64),host.Context.Operator.Id,host.Context.Clock.GetUtcNow().UtcDateTime);
        host.Reader.Reference=ArquitecturaBase.Application.Modules.WhatsApp.Services.WhatsAppAssistantSuggestionPolicy.Reference(row);
        host.Reader.Outgoing=new(outgoingId,originalId,host.Context.Operator.Id,"Completá el formulario.",host.Context.Clock.GetUtcNow().UtcDateTime,
            WhatsAppOutgoingMessageState.Accepted,"wamid.fixture",WhatsAppOutgoingMessageKind.Flow);
        var second=host.Context.Users.AddUser("second-operator@example.test");host.Context.Current.UserId=second.Id;
        host.Context.Permissions.Permissions[second.Id]=[WhatsAppPermissions.Read,WhatsAppPermissions.Send];
        host.Context.Clock.Advance(TimeSpan.FromDays(91));var gets=host.Assistant.Gets;var transactions=host.UnitOfWork.Transactions;
        var result=await host.Service.SendAsync(Send(host),TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(originalId,result.Value.RequestId);Assert.Equal(host.Context.Operator.Id,result.Value.OperatorUserId);
        Assert.Equal(gets,host.Assistant.Gets);Assert.Equal(transactions,host.UnitOfWork.Transactions);Assert.Empty(host.Conversation.Outgoing);
    }
    [Fact]
    public async Task An_operator_without_current_send_permission_cannot_confirm_a_shared_proposal()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();host.Context.Permissions.Permissions[host.Context.Operator.Id]=[WhatsAppPermissions.Read];
        var result=await host.Service.SendAsync(Send(host),TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(WhatsAppAssistantErrors.Forbidden.Code,result.Error.Code);Assert.Empty(host.Conversation.Outgoing);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Duplicate_or_unobserved_field_ids_are_rejected_without_enqueuing(bool duplicate)
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();var field=host.Forms.Prefill.Value.Fields[0].FieldId;
        var result=await host.Service.SendAsync(Send(host,duplicate?[field,field]:[Guid.NewGuid()]),TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(WhatsAppAssistantErrors.InvalidRequest.Code,result.Error.Code);Assert.Empty(host.Conversation.Outgoing);
    }
    [Fact]
    public async Task A_mode_revision_change_prevents_accepting_the_old_interpretation()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var result=await host.Service.SendAsync(Send(host),TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(WhatsAppAssistantErrors.StateConflict.Code,result.Error.Code);Assert.Empty(host.Conversation.Outgoing);
    }
    [Fact]
    public async Task An_owner_rejection_during_confirmation_cannot_send_the_earlier_preview()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();host.Forms.ForSendingPrefill=FlowErrors.StateConflict;
        var result=await host.Service.SendAsync(Send(host),TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(FlowErrors.StateConflict.Code,result.Error.Code);Assert.Empty(host.Conversation.Outgoing);
        Assert.True(host.Forms.LastPrefill!.ForSending);
    }
    [Fact]
    public async Task Expired_source_content_cannot_be_copied_into_a_new_session()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();host.Context.Clock.Advance(TimeSpan.FromDays(91));
        var result=await host.Service.SendAsync(Send(host),TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(WhatsAppAssistantErrors.StateConflict.Code,result.Error.Code);Assert.Empty(host.Sessions.Rows);
    }
    [Fact]
    public async Task A_discarded_suggestion_is_durable_and_cannot_be_sent_again()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();var discarded=await host.Service.DiscardAsync(host.Context.Contact.Id,host.Request.RequestId,TestContext.Current.CancellationToken);
        Assert.True(discarded.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Discarded,discarded.Value.State);
        var result=await host.Service.SendAsync(Send(host),TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(WhatsAppAssistantErrors.StateConflict.Code,result.Error.Code);Assert.Empty(host.Conversation.Outgoing);
    }
}
