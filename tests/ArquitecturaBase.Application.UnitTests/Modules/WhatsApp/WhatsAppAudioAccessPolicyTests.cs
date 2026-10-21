using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;
public sealed class WhatsAppAudioAccessPolicyTests
{
    [Fact]
    public async Task Manual_transcription_needs_only_read_and_captures_no_tools_or_text_model_requirements()
    {
        var f=new WhatsAppAudioFixture();var actor=await f.Access.RequireOperatorAsync(TestContext.Current.CancellationToken);
        Assert.True(actor.IsSuccess);var captured=await f.Access.CaptureAsync(f.Row(),actor.Value,false,TestContext.Current.CancellationToken);
        Assert.True(captured.IsSuccess);Assert.Equal(f.Actor,captured.Value.ActorUserId);Assert.Equal(f.Subject,captured.Value.SubjectUserId);
        Assert.Equal([WhatsAppPermissions.Read],captured.Value.RequiredPermissions.Actor);Assert.Empty(captured.Value.RequiredPermissions.Subject);
        Assert.Empty(captured.Value.AllowedCapabilities);Assert.Empty(captured.Value.AllowedTerminals);
        Assert.Equal(AssistantInteractionMode.Manual, captured.Value.InteractionMode);
    }
    [Fact]
    public async Task Unlinked_manual_audio_uses_operator_and_business_presentation()
    {
        var f=new WhatsAppAudioFixture();f.Contact.UnlinkUser();f.Settings.Presentation=f.Settings.Presentation with {DefaultCulture="en",DefaultTimeZoneId="America/New_York"};
        var captured=await f.Access.CaptureAsync(f.Row(),f.Actor,false,TestContext.Current.CancellationToken);
        Assert.True(captured.IsSuccess);Assert.Null(captured.Value.SubjectUserId);Assert.Equal("en",captured.Value.Culture);Assert.Equal("America/New_York",captured.Value.TimeZoneId);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_or_inactive_operator_is_denied(bool inactive)
    {
        var f=new WhatsAppAudioFixture();if(inactive)await f.Users.SetActiveAsync(f.Actor,false,TestContext.Current.CancellationToken);else f.Current.UserId=null;
        Assert.True((await f.Access.RequireOperatorAsync(TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task Send_without_read_cannot_normalize_audio()
    {
        var f=new WhatsAppAudioFixture();f.Permissions.Permissions[f.Actor]=[WhatsAppPermissions.Send];
        Assert.True((await f.Access.RequireOperatorAsync(TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task Automatic_normalization_uses_the_active_linked_subject_without_operator_permissions()
    {
        var f=new WhatsAppAudioFixture();f.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var captured=await f.Access.CaptureAsync(f.Row(),null,false,TestContext.Current.CancellationToken);
        Assert.True(captured.IsSuccess);Assert.Equal(f.Subject,captured.Value.ActorUserId);Assert.Equal(f.Subject,captured.Value.SubjectUserId);Assert.Empty(captured.Value.RequiredPermissions.Actor);Assert.Equal(AssistantInteractionMode.Assistant,captured.Value.InteractionMode);
    }
    [Fact]
    public async Task Automatic_normalization_cannot_start_in_manual_mode()
    {
        var f=new WhatsAppAudioFixture();Assert.True((await f.Access.CaptureAsync(f.Row(),null,false,TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task Automatic_normalization_cannot_borrow_an_unlinked_or_disabled_subject()
    {
        var f=new WhatsAppAudioFixture();f.Contact.ChangeMode(WhatsAppConversationMode.Assistant);await f.Users.SetActiveAsync(f.Subject,false,TestContext.Current.CancellationToken);
        Assert.True((await f.Access.CaptureAsync(f.Row(),null,false,TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task Captured_audio_can_finish_after_taking_manual_without_a_new_interpretation_context()
    {
        var f=new WhatsAppAudioFixture();f.Contact.ChangeMode(WhatsAppConversationMode.Assistant);var captured=await f.Access.CaptureAsync(f.Row(),null,false,TestContext.Current.CancellationToken);
        Assert.True(captured.IsSuccess);f.Message.RequestAudioNormalization(f.Subject,JsonSerializer.Serialize(captured.Value,JsonSerializerOptions.Web),f.Contact.LinkRevision,WhatsAppAudioFixture.Now.AddDays(90),WhatsAppAudioFixture.Now);
        f.Contact.ChangeMode(WhatsAppConversationMode.Manual);var result=await f.Access.RevalidateAsync(f.Row(),TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal("whatsapp-assistant",result.Value.ChannelId);Assert.Equal(AssistantInteractionMode.Assistant,result.Value.InteractionMode);
    }
    [Fact]
    public async Task Revoked_read_hides_the_captured_manual_normalization()
    {
        var f=new WhatsAppAudioFixture();var context=new AssistantChannelContext("whatsapp-manual",f.Actor,f.Subject,"es","UTC",new([WhatsAppPermissions.Read],[]),[],[]) { InteractionMode = AssistantInteractionMode.Manual };
        f.Message.RequestAudioNormalization(f.Actor,JsonSerializer.Serialize(context,JsonSerializerOptions.Web),f.Contact.LinkRevision,WhatsAppAudioFixture.Now.AddDays(90),WhatsAppAudioFixture.Now);
        f.Permissions.Permissions[f.Actor]=[];Assert.True((await f.Access.RevalidateAsync(f.Row(),TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task Link_revision_change_invalidates_the_captured_audio()
    {
        var f=new WhatsAppAudioFixture();var context=new AssistantChannelContext("whatsapp-manual",f.Actor,f.Subject,"es","UTC",new([WhatsAppPermissions.Read],[]),[],[]) { InteractionMode = AssistantInteractionMode.Manual };
        f.Message.RequestAudioNormalization(f.Actor,JsonSerializer.Serialize(context,JsonSerializerOptions.Web),f.Contact.LinkRevision,WhatsAppAudioFixture.Now.AddDays(90),WhatsAppAudioFixture.Now);
        f.Contact.UnlinkUser();Assert.True((await f.Access.RevalidateAsync(f.Row(),TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task Stored_scope_cannot_add_tools_to_a_normalization_request()
    {
        var f=new WhatsAppAudioFixture();var context=new AssistantChannelContext("whatsapp-manual",f.Actor,f.Subject,"es","UTC",new([WhatsAppPermissions.Read],[]),[new("forms.assistant-candidates",1)],[]) { InteractionMode = AssistantInteractionMode.Manual };
        f.Message.RequestAudioNormalization(f.Actor,JsonSerializer.Serialize(context,JsonSerializerOptions.Web),f.Contact.LinkRevision,WhatsAppAudioFixture.Now.AddDays(90),WhatsAppAudioFixture.Now);
        Assert.True((await f.Access.RevalidateAsync(f.Row(),TestContext.Current.CancellationToken)).IsFailure);
    }
    [Theory]
    [InlineData(AssistantInteractionMode.Assistant)]
    [InlineData(AssistantInteractionMode.Unspecified)]
    public async Task A_crossed_audio_interaction_mode_is_rejected_even_when_channel_and_actor_match(AssistantInteractionMode mode)
    {
        var f = new WhatsAppAudioFixture();
        var captured = await f.Access.CaptureAsync(f.Row(), f.Actor, false, TestContext.Current.CancellationToken);
        Assert.True(captured.IsSuccess);
        var context = captured.Value with { InteractionMode = mode };
        f.Message.RequestAudioNormalization(f.Actor, JsonSerializer.Serialize(context, JsonSerializerOptions.Web),
            f.Contact.LinkRevision, WhatsAppAudioFixture.Now.AddDays(90), WhatsAppAudioFixture.Now);
        var result = await f.Access.RevalidateAsync(f.Row(), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(WhatsAppChatErrors.RequestConflict, result.Error);
    }
}
