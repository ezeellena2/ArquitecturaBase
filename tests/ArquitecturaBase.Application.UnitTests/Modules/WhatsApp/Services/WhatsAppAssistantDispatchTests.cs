using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAssistantDispatchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_automatic_send_reserves_the_existing_queue_without_a_manual_operator()
    {
        var host = await WhatsAppAssistantDispatchFixture.CreateAsync();
        var result = await host.Service.ReserveAsync(Ct);
        Assert.True(result.IsSuccess); Assert.True(result.Value.Progressed); Assert.NotNull(result.Value.Operation);
        Assert.Equal(WhatsAppMessageOrigin.Assistant,result.Value.Operation.Origin);
        Assert.NotNull(result.Value.Operation.DispatchReservationId); Assert.Null(host.Outgoing.OperatorUserId);
        Assert.Equal(WhatsAppOutgoingMessageState.Sending,host.Outgoing.State);
        Assert.Null(host.Outgoing.MessageEffectStartedAtUtc); Assert.Equal(1,host.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task A_current_reservation_is_revalidated_and_marks_the_post_only_once()
    {
        var host = await WhatsAppAssistantDispatchFixture.CreateAsync();
        var reservation = host.Outgoing.ReserveAssistant(host.Now);
        Assert.True((await host.Service.MarkAssistantMessageEffectAsync(host.Context.Contact.Id,host.Outgoing.Id,reservation,Ct)).Value);
        Assert.Equal(host.Now,host.Outgoing.MessageEffectStartedAtUtc);
        Assert.False((await host.Service.MarkAssistantMessageEffectAsync(host.Context.Contact.Id,host.Outgoing.Id,reservation,Ct)).Value);
    }

    [Theory]
    [InlineData("manual")] [InlineData("revision")] [InlineData("link")] [InlineData("inactive")]
    [InlineData("window")] [InlineData("content")] [InlineData("missing-reference")]
    public async Task Changed_authority_before_the_post_cancels_the_accepted_send(string change)
    {
        var host = await WhatsAppAssistantDispatchFixture.CreateAsync();
        if (change == "window") host.Context.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(30));
        var reservation = host.Outgoing.ReserveAssistant(host.Now);
        switch (change)
        {
            case "manual": host.Context.Contact.ChangeMode(WhatsAppConversationMode.Manual); break;
            case "revision": host.Context.Contact.ChangeMode(WhatsAppConversationMode.Manual);host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);break;
            case "link": host.Context.Contact.UnlinkUser();host.Context.Contact.LinkUser(host.Context.Subject.Id);break;
            case "inactive": await host.Context.Users.SetActiveAsync(host.Context.Subject.Id,false,Ct);break;
            case "window": host.Context.Clock.Advance(TimeSpan.FromSeconds(31));break;
            case "content": host.Reader.Reference=host.Reader.Reference! with {ContentExpiresAtUtc=host.Now};break;
            default: host.Reader.Reference=null;break;
        }
        var result = await host.Service.MarkAssistantMessageEffectAsync(host.Context.Contact.Id,host.Outgoing.Id,reservation,Ct);
        Assert.True(result.IsSuccess); Assert.False(result.Value); Assert.Null(host.Outgoing.MessageEffectStartedAtUtc);
        Assert.Equal(WhatsAppOutgoingMessageState.Failed,host.Outgoing.State);
    }

    [Fact]
    public async Task A_stale_completion_cannot_accept_another_reservation()
    {
        var host = await WhatsAppAssistantDispatchFixture.CreateAsync();
        var first = host.Outgoing.ReserveAssistant(host.Now);host.Outgoing.RecoverExpiredAssistant();
        var current = host.Outgoing.ReserveAssistant(host.Now);
        var result = await host.Service.CompleteAsync(host.Context.Contact.Id,host.Outgoing.Id,"wamid.stale",false,Ct,first);
        Assert.True(result.IsFailure);Assert.Null(host.Outgoing.WaMessageId);
        Assert.True(host.Outgoing.OwnsAssistantReservation(current));Assert.Empty(host.Messages.Added);
    }

    [Fact]
    public async Task An_already_started_post_can_confirm_after_manual_takeover_and_keeps_its_real_origin()
    {
        var host = await WhatsAppAssistantDispatchFixture.CreateAsync();
        var reservation = host.Outgoing.ReserveAssistant(host.Now);host.Outgoing.MarkAssistantMessageEffect(reservation,host.Now);
        host.Context.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        var result = await host.Service.CompleteAsync(host.Context.Contact.Id,host.Outgoing.Id,"wamid.assistant-known",false,Ct,reservation);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppOutgoingMessageState.Accepted,host.Outgoing.State);
        var message=Assert.Single(host.Messages.Added);Assert.Equal(WhatsAppMessageOrigin.Assistant,message.Origin);
        Assert.Null(message.OperatorUserId);Assert.Equal("wamid.assistant-known",message.WaMessageId);
    }

    [Fact]
    public async Task A_second_start_cannot_erase_an_effect_already_in_flight_after_manual_takeover()
    {
        var host = await WhatsAppAssistantDispatchFixture.CreateAsync();
        var reservation = host.Outgoing.ReserveAssistant(host.Now);
        host.Outgoing.MarkAssistantMessageEffect(reservation,host.Now);
        host.Context.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        var result = await host.Service.MarkAssistantMessageEffectAsync(host.Context.Contact.Id,host.Outgoing.Id,reservation,Ct);
        Assert.False(result.Value);Assert.Equal(WhatsAppOutgoingMessageState.Sending,host.Outgoing.State);
        Assert.Equal(host.Now,host.Outgoing.MessageEffectStartedAtUtc);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Expired_sends_recover_by_whether_the_message_post_really_started(bool posted)
    {
        var host = await WhatsAppAssistantDispatchFixture.CreateAsync();
        var reservation=host.Outgoing.ReserveAssistant(host.Now);
        if(posted)host.Outgoing.MarkAssistantMessageEffect(reservation,host.Now);
        host.Context.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.True((await host.Service.RecoverExpiredAsync(Ct)).IsSuccess);
        Assert.Equal(posted?WhatsAppOutgoingMessageState.Uncertain:WhatsAppOutgoingMessageState.Pending,host.Outgoing.State);
        Assert.Empty(host.Messages.Added);
    }
    [Theory]
    [InlineData("current",true)] [InlineData("revision",false)] [InlineData("unavailable",false)] [InlineData("declined",false)] [InlineData("request",false)]
    public async Task Clarification_buttons_keep_the_observed_form_revision_and_current_eligibility(string change,bool allowed)
    {
        var host=await WhatsAppAssistantDispatchFixture.CreateAsync();
        var first=new StructuredFormSelectionRow(Guid.NewGuid(),Guid.NewGuid(),1,"VISITA",new Dictionary<string,string>{{"es","Visita"}},2,new Dictionary<string,string>{{"es","Enviar"}}){DefinitionRevision=3};
        var second=first with{DefinitionId=Guid.NewGuid(),VersionId=Guid.NewGuid()};
        host.Forms.AcceptedForms=[first,second];
        var request=host.Reader.Reference!.RequestId;
        var buttons=new[]{new WhatsAppOutgoingQuickReply(WhatsAppAssistantReplyCodec.Encode(request,first.DefinitionId,first.VersionId),"Visita",3),
            new WhatsAppOutgoingQuickReply(WhatsAppAssistantReplyCodec.Encode(request,second.DefinitionId,second.VersionId),"Otra visita",3)};
        var outgoing=WhatsAppOutgoingMessage.CreateAssistantQuickReplies(host.Context.Subject.Id,host.Context.Contact.Id,request,"¿Cuál querés completar?",buttons,host.Now,
            host.Context.Contact.LinkRevision,host.Context.Contact.WaId!,host.Context.Contact.ModeRevision);
        if(change=="revision")host.Forms.AcceptedForms=[first with{DefinitionRevision=4},second];
        if(change=="unavailable")host.Forms.AcceptedForms=[second];
        if(change=="declined")host.Reader.Declined=true;
        if(change=="request")host.Reader.Reference=host.Reader.Reference with{RequestId=Guid.NewGuid()};
        var result=await host.Guard.ValidateAsync(host.Context.Contact,outgoing,Ct);
        Assert.Equal(allowed,result.IsSuccess);
    }
    [Theory]
    [InlineData(1,"own",true)] [InlineData(3,"own",true)] [InlineData(2,"another",false)] [InlineData(2,"swapped",false)] [InlineData(2,"mixed",false)]
    public async Task A_case_question_goes_out_with_one_to_three_buttons_of_its_own_reference_in_their_place(int count,string ids,bool allowed)
    {
        var host=await WhatsAppAssistantDispatchFixture.CreateAsync();
        var request=host.Reader.Reference!.RequestId;
        var buttons=Enumerable.Range(0,count).Select(place=>new WhatsAppOutgoingQuickReply(ids=="mixed"&&place==1?"opaque-choice":
            WhatsAppAssistantCaseButtonCodec.Encode(ids=="another"&&place==1?Guid.NewGuid():request,ids=="swapped"?count-1-place:place),$"Opción {place+1}",0)).ToArray();
        var outgoing=WhatsAppOutgoingMessage.CreateAssistantQuickReplies(host.Context.Subject.Id,host.Context.Contact.Id,request,"¿Registrar la visita?",buttons,host.Now,
            host.Context.Contact.LinkRevision,host.Context.Contact.WaId!,host.Context.Contact.ModeRevision);
        // As WhatsAppAssistantReplyPolicy leaves it: the question is the outgoing its own reference accepted.
        host.Reader.Reference=host.Reader.Reference with{OutgoingId=outgoing.Id};
        var result=await host.Guard.ValidateAsync(host.Context.Contact,outgoing,Ct);
        Assert.Equal(allowed,result.IsSuccess);
    }
    // IA3-A: a form or catalog summary carries «Crear» and an import summary (IA, Stage 2) «Revisar importación», each with «Cambiar algo».
    [Theory]
    [InlineData(WhatsAppAssistantAuthoringReplyCodec.Create)] [InlineData(WhatsAppAssistantAuthoringReplyCodec.Review)]
    public async Task An_authoring_summary_goes_out_with_its_action_and_change(string action)
    {
        var host=await WhatsAppAssistantDispatchFixture.CreateAsync();
        var reference=host.Reader.Reference!;
        var proposal=reference.ProposalId!.Value;
        var buttons=new[]{new WhatsAppOutgoingQuickReply(WhatsAppAssistantAuthoringReplyCodec.Encode(reference.RequestId,proposal,action),
                action==WhatsAppAssistantAuthoringReplyCodec.Create?"Crear borrador":"Revisar importación",0),
            new WhatsAppOutgoingQuickReply(WhatsAppAssistantAuthoringReplyCodec.Encode(reference.RequestId,proposal,WhatsAppAssistantAuthoringReplyCodec.Change),"Cambiar algo",0)};
        var outgoing=WhatsAppOutgoingMessage.CreateAssistantQuickReplies(host.Context.Subject.Id,host.Context.Contact.Id,reference.RequestId,"Te propongo este formulario.",buttons,host.Now,
            host.Context.Contact.LinkRevision,host.Context.Contact.WaId!,host.Context.Contact.ModeRevision);
        host.Reader.Reference=reference with{OutgoingId=outgoing.Id};
        Assert.True((await host.Guard.ValidateAsync(host.Context.Contact,outgoing,Ct)).IsSuccess);
    }
}
