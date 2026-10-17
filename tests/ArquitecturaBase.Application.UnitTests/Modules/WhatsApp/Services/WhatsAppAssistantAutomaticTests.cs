using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAssistantAutomaticTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static WhatsAppAssistantDispatchService Service(WhatsAppAssistantSuggestionFixture host) => new(host.Context.Policy,
        new(host.Reader,host.Assistant,host.Forms,host.Context.Clock,host.Presentation),host.Reader,host.Requests,host.Conversation,host.UnitOfWork,WhatsAppAssistantAcceptanceFixture.CreateAutomatic(host),
        NullLogger<WhatsAppAssistantDispatchService>.Instance);

    private static async Task<WhatsAppAssistantSuggestionFixture> HostAsync(bool withReference = true)
    {
        var host = await WhatsAppAssistantSuggestionFixture.ReadyAsync();
        host.Requests.Rows.Clear();
        host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var captured = await host.Context.Policy.CaptureAsync(host.Context.Contact,null,Ct);
        Assert.True(captured.IsSuccess);
        if (withReference)
        {
            var source = host.Reader.Source!;
            var row = WhatsAppAssistantRequest.Create(host.Request.RequestId,host.Context.Contact.Id,source.Id,
                host.Context.Subject.Id,host.Context.Subject.Id,WhatsAppAssistantRequestKind.Automatic,
                host.Context.Contact.ModeRevision,host.Context.Contact.LinkRevision,
                JsonSerializer.Serialize(captured.Value.Context,JsonSerializerOptions.Web),source.OccurredAtUtc,source.ContentExpiresAtUtc);
            host.Requests.Add(row);
            host.Reader.Reference = WhatsAppAssistantSuggestionPolicy.Reference(row);
        }
        else host.Reader.Reference = null;
        host.Assistant.Outcome = host.Assistant.Outcome.Value with { Provenance = host.Assistant.Outcome.Value.Provenance! with { ChannelId = "whatsapp-assistant" } };
        return host;
    }

    [Fact]
    public async Task Automatic_preparation_captures_the_linked_subject_without_an_operator_or_home_permission()
    {
        var host = await HostAsync(false);
        var result = await Service(host).PrepareAsync(host.Context.Contact.Id,Ct);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(host.Context.Subject.Id,result.Value.Operation.Context.ActorUserId);
        Assert.Equal(host.Context.Subject.Id,result.Value.Operation.Context.SubjectUserId);
        Assert.Equal("whatsapp-assistant",result.Value.Operation.Context.ChannelId);
        Assert.Equal(11,result.Value.Operation.Context.AllowedCapabilities.Count);
        Assert.Empty(result.Value.Operation.Context.RequiredPermissions.Actor);
        Assert.Empty(result.Value.Operation.Context.RequiredPermissions.Subject);
        Assert.Equal(host.Reader.Source!.Body,result.Value.Operation.Text);
        Assert.Single(host.Requests.Rows);
    }

    [Fact]
    public async Task Preparing_again_recovers_the_same_automatic_operation_before_any_quota_check()
    {
        var host = await HostAsync(false); host.Reader.OfferCount = 2;
        var service = Service(host);
        var first = await service.PrepareAsync(host.Context.Contact.Id,Ct);
        var second = await service.PrepareAsync(host.Context.Contact.Id,Ct);
        Assert.NotNull(first.Value); Assert.NotNull(second.Value);
        Assert.Equal(first.Value.RequestId,second.Value.RequestId);
        Assert.Single(host.Requests.Rows);
    }

    [Fact]
    public async Task Automatic_registration_uses_the_existing_session_and_outbox_with_explicit_assistant_origin()
    {
        var host = await HostAsync();
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct)).IsSuccess);
        var outgoing = Assert.Single(host.Conversation.Outgoing);
        var session = Assert.Single(host.Sessions.Rows);
        Assert.Equal(WhatsAppMessageOrigin.Assistant,outgoing.Origin);
        Assert.Null(outgoing.OperatorUserId); Assert.Null(session.OperatorUserId);
        Assert.Equal(host.Context.Subject.Id,outgoing.RecipientUserId);
        Assert.Equal(host.Context.Contact.ModeRevision,outgoing.AssistantModeRevision);
        Assert.Equal(WhatsAppMessageOrigin.Assistant,session.Origin);
        Assert.Equal(WhatsAppAssistantRequestState.Sent,host.Requests.Rows[0].State);
        Assert.Equal(host.Forms.Prefill.Value.Fields.Select(field=>field.FieldId),host.Protector.Content!.Origin.IncludedFieldIds);
        Assert.Equal("whatsapp-assistant",host.Protector.Content.Origin.Proposal!.Provenance.ChannelId);
        Assert.Equal(0,host.Assistant.Submits);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Quota_or_prior_decline_prevents_the_offer_after_discovery_without_recording_a_missing_form(bool declined)
    {
        var host = await HostAsync(); host.Reader.OfferCount = declined ? 0 : 2; host.Reader.Declined = declined;
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);
        Assert.Empty(host.Conversation.Outgoing); Assert.Empty(host.Sessions.Rows);
        Assert.True(host.Forms.Previews > 0);
    }

    [Fact]
    public async Task A_manual_takeover_while_the_model_is_running_prevents_the_automatic_effect()
    {
        var host = await HostAsync(); host.Context.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);
        Assert.Empty(host.Conversation.Outgoing);
    }

    [Fact]
    public async Task A_newer_business_receipt_prevents_sending_the_old_interpretation()
    {
        var host = await HostAsync(); host.Reader.Source = host.Reader.Source! with { Id = Guid.NewGuid() };
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);
        Assert.Empty(host.Conversation.Outgoing);
    }

    [Fact]
    public async Task No_reply_stays_silent_even_when_the_offer_quota_is_already_exhausted()
    {
        var host = await HostAsync(); host.Reader.OfferCount = 2;
        var proposal = new AssistantObservedProposal(Guid.NewGuid(),Guid.NewGuid(),new("whatsapp.no-reply",1),new WhatsAppAssistantNoReplyPayload("not_form"));
        var before = host.Context.Contact.LastInboundAtUtc;
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome.Value with { Proposal = proposal },Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.NoReply,host.Requests.Rows[0].State);
        Assert.Equal(before,host.Context.Contact.LastInboundAtUtc); Assert.Empty(host.Conversation.Outgoing);
    }

    [Fact]
    public async Task A_completion_replay_does_not_consume_another_slot_or_repeat_a_confirmed_offer()
    {
        var host = await HostAsync(); var service = Service(host);
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct)).IsSuccess);
        host.Reader.OfferCount = 2;
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Single(host.Conversation.Outgoing); Assert.Single(host.Sessions.Rows);
    }

    [Fact]
    public async Task Current_owner_rejection_at_acceptance_discards_the_earlier_preview()
    {
        var host = await HostAsync(); host.Forms.ForSendingPrefill = FlowErrors.StateConflict;
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);
        Assert.Empty(host.Conversation.Outgoing); Assert.Empty(host.Sessions.Rows);
    }
    [Fact]
    public async Task The_offer_day_uses_the_captured_business_timezone_instead_of_the_operator_timezone()
    {
        var host=await HostAsync();
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct)).IsSuccess);
        var count=Assert.IsType<(DateTime FromUtc,DateTime ToUtc,Guid? ExcludingRequestId)>(host.Reader.LastCount);
        Assert.Equal(new DateTime(2026,10,17,4,0,0,DateTimeKind.Utc),count.FromUtc);
        Assert.Equal(new DateTime(2026,10,18,4,0,0,DateTimeKind.Utc),count.ToUtc);
        Assert.Equal(host.Request.RequestId,count.ExcludingRequestId);
    }
    [Fact]
    public async Task An_earlier_automatic_send_without_a_result_prevents_a_second_automatic_enqueue()
    {
        var host=await HostAsync();host.Reader.HasUnresolvedOffer=true;
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);
        Assert.Empty(host.Conversation.Outgoing);
    }
    [Fact]
    public async Task A_manual_send_without_a_result_also_prevents_a_new_automatic_enqueue()
    {
        var host=await HostAsync();host.Conversation.Unresolved=true;
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);
        Assert.Empty(host.Conversation.Outgoing);
    }
}
