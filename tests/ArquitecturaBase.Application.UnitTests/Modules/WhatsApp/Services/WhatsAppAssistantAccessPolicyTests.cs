using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAssistantAccessPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task A_real_operator_needs_only_chat_read_and_send_without_a_Home_permission()
    { var h=new WhatsAppAssistantContextFixture();var result=await h.Policy.RequireOperatorAsync(Ct);Assert.True(result.IsSuccess);Assert.Equal(h.Operator.Id,result.Value.Id); }
    [Fact]
    public async Task A_manual_suggestion_has_no_actor_before_a_signed_in_operator_requests_it()
    { var h=new WhatsAppAssistantContextFixture();h.Current.UserId=null;var result=await h.Policy.RequireOperatorAsync(Ct);Assert.Equal(ErrorType.Unauthorized,result.Error.Type); }
    [Theory]
    [InlineData(WhatsAppPermissions.Read)]
    [InlineData(WhatsAppPermissions.Send)]
    public async Task A_partial_operator_permission_does_not_authorize_a_suggestion(string only)
    { var h=new WhatsAppAssistantContextFixture();h.Permissions.Permissions[h.Operator.Id]=[only];Assert.Equal(ErrorType.Forbidden,(await h.Policy.RequireOperatorAsync(Ct)).Error.Type); }
    [Fact]
    public async Task Automatic_context_uses_the_recipient_and_the_business_zone_without_inheriting_operator_permissions()
    {
        var h=new WhatsAppAssistantContextFixture();h.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var result=await h.Policy.CaptureAsync(h.Contact,null,Ct);Assert.True(result.IsSuccess);var context=result.Value.Context;
        Assert.Equal(h.Subject.Id,context.ActorUserId);Assert.Equal(h.Subject.Id,context.SubjectUserId);
        Assert.Equal("America/New_York",context.TimeZoneId);Assert.NotEqual(h.Subject.TimeZoneId,context.TimeZoneId);
        Assert.Empty(context.RequiredPermissions.Actor);Assert.Empty(context.RequiredPermissions.Subject);
        Assert.Equal("whatsapp-assistant",context.ChannelId);Assert.Equal(AssistantInteractionMode.Assistant,context.InteractionMode);
    }
    [Fact]
    public async Task Manual_context_keeps_the_operator_and_subject_separate_without_globally_requiring_form_creation()
    {
        var h=new WhatsAppAssistantContextFixture();var result=await h.Policy.CaptureAsync(h.Contact,h.Operator.Id,Ct);Assert.True(result.IsSuccess);var context=result.Value.Context;
        Assert.Equal(h.Operator.Id,context.ActorUserId);Assert.Equal(h.Subject.Id,context.SubjectUserId);
        Assert.Equal(new[]{WhatsAppPermissions.Read,WhatsAppPermissions.Send},context.RequiredPermissions.Actor);Assert.Empty(context.RequiredPermissions.Subject);
        Assert.Equal("whatsapp-manual",context.ChannelId);Assert.Equal(AssistantInteractionMode.Manual,context.InteractionMode);
        Assert.Equal(h.Contact.Id,context.ChannelConversationId);
    }
    [Theory]
    [InlineData(false,7)]
    [InlineData(true,9)]
    public async Task Only_the_automatic_channel_offers_catalog_filter_proposals(bool automatic,int count)
    {
        var h=new WhatsAppAssistantContextFixture();if(automatic)h.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var captured=await h.Policy.CaptureAsync(h.Contact,automatic?null:h.Operator.Id,Ct);
        Assert.True(captured.IsSuccess);Assert.Equal(count,captured.Value.Context.AllowedCapabilities.Count);
        Assert.Equal(automatic,captured.Value.Context.AllowedCapabilities.Any(capability=>capability.Id=="forms.catalog-filters.metadata"));
        Assert.Equal(automatic,captured.Value.Context.AllowedCapabilities.Any(capability=>capability.Id=="forms.catalog-filters.propose"));
        Assert.Contains(captured.Value.Context.AllowedCapabilities,capability=>capability.Id=="forms.missing-request.propose");
    }
    [Fact]
    public async Task Only_a_captured_response_context_offers_C4_and_fixes_the_exact_source()
    {
        var h = new WhatsAppAssistantContextFixture();
        var captured = await h.Policy.CaptureAsync(h.Contact, h.Operator.Id, Ct);
        Assert.True(captured.IsSuccess);
        Assert.Null(captured.Value.Context.SourceRecordId);
        Assert.DoesNotContain(captured.Value.Context.AllowedCapabilities, capability => capability.Id is "forms.suggestion-candidates" or "forms.propose-suggestion");
        var source = Guid.NewGuid();
        var c4 = WhatsAppAssistantAccessPolicy.ForCapturedRecord(captured.Value.Context, source);
        Assert.Equal(source, c4.SourceRecordId);
        Assert.Equal(AssistantInteractionMode.Manual, c4.InteractionMode);
        Assert.Contains(c4.AllowedCapabilities, capability => capability.Id == "forms.suggestion-candidates");
        Assert.DoesNotContain(c4.AllowedCapabilities, capability => capability.Id == "forms.assistant-candidates");
        var roundtrip = JsonSerializer.Deserialize<ArquitecturaBase.Application.Models.Assistant.AssistantChannelContext>(JsonSerializer.Serialize(c4, JsonSerializerOptions.Web), JsonSerializerOptions.Web);
        Assert.Equal(source, roundtrip!.SourceRecordId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_context_must_match_the_selected_mode(bool automatic)
    {
        var h=new WhatsAppAssistantContextFixture();if(!automatic)h.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        Assert.True((await h.Policy.CaptureAsync(h.Contact,automatic?null:h.Operator.Id,Ct)).IsFailure);
    }
    [Fact]
    public async Task An_unlinked_contact_is_not_an_automatic_identity()
    { var h=new WhatsAppAssistantContextFixture();h.Contact.UnlinkUser();h.Contact.ChangeMode(WhatsAppConversationMode.Assistant);Assert.True((await h.Policy.CaptureAsync(h.Contact,null,Ct)).IsFailure); }
    [Fact]
    public async Task A_closed_customer_window_blocks_an_automatic_context()
    {var h=new WhatsAppAssistantContextFixture();h.Contact.ChangeMode(WhatsAppConversationMode.Assistant);h.Clock.Advance(TimeSpan.FromHours(24));Assert.True((await h.Policy.CaptureAsync(h.Contact,null,Ct)).IsFailure);}
    [Fact]
    public async Task An_unchanged_reference_can_be_revalidated_without_a_model_call()
    {var h=new WhatsAppAssistantContextFixture();var captured=await h.Policy.CaptureAsync(h.Contact,h.Operator.Id,Ct);Assert.True(captured.IsSuccess);Assert.Null(await h.Policy.RevalidateAsync(h.Contact,Reference(h,captured.Value.Context),Ct));}
    [Fact]
    public async Task An_unlink_and_relink_to_the_same_account_invalidates_the_sealed_reference()
    {var h=new WhatsAppAssistantContextFixture();var captured=await h.Policy.CaptureAsync(h.Contact,h.Operator.Id,Ct);Assert.True(captured.IsSuccess);var row=Reference(h,captured.Value.Context);h.Contact.UnlinkUser();h.Contact.LinkUser(h.Subject.Id);Assert.NotNull(await h.Policy.RevalidateAsync(h.Contact,row,Ct));}
    [Theory]
    [InlineData(AssistantInteractionMode.Assistant)]
    [InlineData(AssistantInteractionMode.Unspecified)]
    public async Task A_crossed_interaction_mode_invalidates_the_captured_reference(AssistantInteractionMode mode)
    {
        var f = new WhatsAppAssistantContextFixture();
        var captured = await f.Policy.CaptureAsync(f.Contact, f.Operator.Id, Ct);
        Assert.True(captured.IsSuccess);
        var reference = Reference(f, captured.Value.Context with { InteractionMode = mode });
        Assert.Equal(WhatsAppAssistantErrors.StateConflict, await f.Policy.RevalidateAsync(f.Contact, reference, Ct));
    }
    private static WhatsAppAssistantReferenceRow Reference(WhatsAppAssistantContextFixture h,ArquitecturaBase.Application.Models.Assistant.AssistantChannelContext context)=>
        new(Guid.CreateVersion7(),h.Contact.Id,Guid.CreateVersion7(),h.Operator.Id,h.Subject.Id,WhatsAppAssistantRequestKind.Manual,h.Contact.ModeRevision,h.Contact.LinkRevision,JsonSerializer.Serialize(context,JsonSerializerOptions.Web),h.Clock.GetUtcNow().UtcDateTime.AddHours(1),WhatsAppAssistantRequestState.Pending,null,null,null);
}
