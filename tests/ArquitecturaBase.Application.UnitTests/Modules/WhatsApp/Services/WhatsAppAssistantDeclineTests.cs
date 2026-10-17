using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAssistantDeclineTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    private static async Task<(WhatsAppAssistantSuggestionFixture Host,WhatsAppAssistantDispatchService Service,Guid Id,Guid DefinitionId)> ArrangeAsync(bool automatic)
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();
        var service=new WhatsAppAssistantDispatchService(host.Context.Policy,new(host.Reader,host.Assistant,host.Forms,host.Context.Clock,host.Presentation),host.Reader,
            host.Requests,host.Conversation,host.UnitOfWork,WhatsAppAssistantAcceptanceFixture.CreateAutomatic(host),NullLogger<WhatsAppAssistantDispatchService>.Instance);
        var id=host.Request.RequestId;
        if(automatic)
        {
            host.Requests.Rows.Clear();host.Reader.Reference=null;host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
            var prepared=await service.PrepareAsync(host.Context.Contact.Id,Ct);id=prepared.Value!.RequestId;
        }
        var offer=new StructuredFormOfferReference(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Visita",false);host.Reader.Offers.Add(offer);
        var turnId=Guid.NewGuid();var reference=host.Reader.Reference!;
        var proposal=new AssistantObservedProposal(turnId,Guid.NewGuid(),new("whatsapp.decline-form",1),new WhatsAppAssistantDeclinePayload(1,offer.RequestId,offer.DefinitionId))
            {ObservedReads=[new(turnId,1,new("whatsapp.offered-forms",1),new StructuredFormOffersPayload(reference.SubjectUserId,"es",[offer],[]))]};
        host.Assistant.Outcome=host.Assistant.Outcome.Value with {RequestId=id,Proposal=proposal,
            Provenance=host.Assistant.Outcome.Value.Provenance! with {ChannelId=automatic?"whatsapp-assistant":"whatsapp-manual"}};
        return(host,service,id,offer.DefinitionId);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_explicit_observed_decline_is_durable_without_a_reply_or_offer_quota(bool automatic)
    {
        var(host,service,id,definitionId)=await ArrangeAsync(automatic);host.Reader.OfferCount=2;
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        var row=Assert.Single(host.Requests.Rows);Assert.Equal(WhatsAppAssistantRequestState.NoReply,row.State);Assert.Equal(definitionId,row.DeclinedDefinitionId);
        var decidedAt=row.DecidedAtUtc;await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Assert.Equal(decidedAt,row.DecidedAtUtc);Assert.Empty(host.Conversation.Outgoing);Assert.Empty(host.Sessions.Rows);Assert.Null(host.Reader.LastCount);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("other-turn")]
    [InlineData("other-subject")]
    [InlineData("withdrawn-offer")]
    public async Task An_unobserved_or_no_longer_owned_offer_is_never_recorded_as_a_decline(string invalid)
    {
        var(host,service,id,_)=await ArrangeAsync(true);var operation=host.Assistant.Outcome.Value;var proposal=operation.Proposal!;
        if(invalid=="missing") proposal=proposal with {ObservedReads=[]};
        if(invalid=="other-turn") proposal=proposal with {TurnId=Guid.NewGuid()};
        if(invalid=="other-subject") proposal=proposal with {ObservedReads=[proposal.ObservedReads[0] with {Payload=new StructuredFormOffersPayload(Guid.NewGuid(),"es",host.Reader.Offers,[])}]};
        if(invalid=="withdrawn-offer") host.Reader.Offers.Clear();
        await service.CompleteAsync(host.Context.Contact.Id,id,operation with {Proposal=proposal},Ct);
        Assert.Null(host.Requests.Rows[0].DeclinedDefinitionId);Assert.Empty(host.Conversation.Outgoing);
    }
}
