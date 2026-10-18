using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppChatModeServiceTests
{
    private const string Configure = WhatsAppPermissions.ConfigureAssistant;
    private const WhatsAppConversationMode AssistantMode = WhatsAppConversationMode.Assistant;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(WhatsAppOutgoingMessageState.Pending)]
    [InlineData(WhatsAppOutgoingMessageState.Sending)]
    [InlineData(WhatsAppOutgoingMessageState.Uncertain)]
    public async Task Send_alone_can_stop_assistance_without_repeating_or_rewriting_an_outgoing_send(WhatsAppOutgoingMessageState state)
    {
        var host = new WhatsAppChatModeFixture([WhatsAppPermissions.Send]);
        host.Repository.Contact.ChangeMode(AssistantMode);
        var outgoing = WhatsAppOutgoingMessage.Create(host.Actor, host.Repository.Contact.Id, Guid.NewGuid(), "Fictitious reply", WhatsAppChatModeFixture.Now);
        if (state is WhatsAppOutgoingMessageState.Sending or WhatsAppOutgoingMessageState.Uncertain) outgoing.Start(WhatsAppChatModeFixture.Now);
        if (state == WhatsAppOutgoingMessageState.Uncertain) outgoing.MarkUncertain();
        host.Repository.Outgoing.Add(outgoing);
        var revision = host.Repository.Contact.ModeRevision;
        var result = await host.Service.ChangeAsync(new(host.Repository.Contact.Id, WhatsAppConversationMode.Manual, revision), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(WhatsAppConversationMode.Manual, result.Value.Mode);
        Assert.Equal(revision + 1, result.Value.ModeRevision);
        Assert.Equal(state, outgoing.State);
        Assert.Equal(0, host.Assistant.Reads);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Taking_manual_attention_reports_only_the_send_already_started_by_the_assistant(bool started)
    {
        var host = new WhatsAppChatModeFixture([WhatsAppPermissions.Send]);
        host.Repository.Contact.ChangeMode(AssistantMode);
        host.Repository.StartedAssistantMessage = started;
        var result = await host.Service.ChangeAsync(new(host.Repository.Contact.Id, WhatsAppConversationMode.Manual, 1), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(started, result.Value.AssistantSendInProgress);
        Assert.Equal(WhatsAppConversationMode.Manual, result.Value.Mode);
        Assert.Equal(1, host.Repository.Locks);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Taking_manual_attention_cancels_pending_model_work_after_commit_without_touching_an_outgoing_effect()
    {
        var host = new WhatsAppChatModeFixture([WhatsAppPermissions.Send]);
        host.Repository.Contact.ChangeMode(AssistantMode);
        var context = new AssistantChannelContext("whatsapp-assistant", host.Actor, host.Actor, "es", "UTC", new([], []), [], []);
        var pending = WhatsAppAssistantRequest.Create(Guid.NewGuid(), host.Repository.Contact.Id, Guid.NewGuid(), host.Actor, host.Actor,
            WhatsAppAssistantRequestKind.Automatic, host.Repository.Contact.ModeRevision, host.Repository.Contact.LinkRevision,
            JsonSerializer.Serialize(context, JsonSerializerOptions.Web), WhatsAppChatModeFixture.Now, WhatsAppChatModeFixture.Now.AddHours(1));
        host.Repository.Pending.Add(pending);
        host.Repository.StartedAssistantMessage = true;
        var result = await host.Service.ChangeAsync(new(host.Repository.Contact.Id, WhatsAppConversationMode.Manual, 1), Ct);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AssistantSendInProgress);
        var canceled = Assert.Single(host.Assistant.Canceled);
        Assert.Equal(context.ChannelId, canceled.Context.ChannelId);
        Assert.Equal(context.ActorUserId, canceled.Context.ActorUserId);
        Assert.Equal(context.SubjectUserId, canceled.Context.SubjectUserId);
        Assert.Equal(pending.RequestId, canceled.RequestId);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Configure_alone_can_enable_assistance_without_manual_send_or_home_permissions()
    {
        var host = new WhatsAppChatModeFixture([Configure]);
        var result = await host.Service.ChangeAsync(new(host.Repository.Contact.Id, AssistantMode, 0), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(AssistantMode, result.Value.Mode);
        Assert.Equal(1, result.Value.ModeRevision);
        Assert.Equal(1, host.Assistant.Reads);
    }

    [Theory]
    [InlineData(WhatsAppPermissions.Read, WhatsAppConversationMode.Manual)]
    [InlineData(Configure, WhatsAppConversationMode.Manual)]
    [InlineData(WhatsAppPermissions.Send, AssistantMode)]
    [InlineData("whatsapp.manage", AssistantMode)]
    [InlineData("whatsapp.manage", WhatsAppConversationMode.Manual)]
    public async Task A_permission_for_another_operation_cannot_change_the_requested_mode(string permission, WhatsAppConversationMode mode)
    {
        var host = new WhatsAppChatModeFixture([permission]);
        var result = await host.Service.ChangeAsync(new(host.Repository.Contact.Id, mode, 0), Ct);
        Assert.Equal(WhatsAppChatErrors.Forbidden, result.Error);
        Assert.Equal(0, host.Repository.Contact.ModeRevision);
        Assert.Equal(0, host.Repository.Locks);
        Assert.Equal(0, host.Assistant.Reads);
        Assert.Equal(0, host.UnitOfWork.Commits);
    }

    [Theory]
    [InlineData("notInstalled")]
    [InlineData("notConfigured")]
    [InlineData("paused")]
    [InlineData("modelNotQualified")]
    public async Task Unavailable_assistance_cannot_be_enabled(string availability)
    {
        var host = new WhatsAppChatModeFixture([Configure]);
        host.Assistant.State = availability;
        var result = await host.Service.ChangeAsync(new(host.Repository.Contact.Id, AssistantMode, 0), Ct);
        Assert.Equal(WhatsAppChatErrors.AssistantUnavailable, result.Error);
        Assert.Equal(WhatsAppConversationMode.Manual, host.Repository.Contact.Mode);
        Assert.Equal(0, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task An_inactive_actor_cannot_change_modes_even_with_the_required_permission()
    {
        var host = new WhatsAppChatModeFixture([WhatsAppPermissions.Send, Configure], active: false);
        var result = await host.Service.ChangeAsync(new(host.Repository.Contact.Id, AssistantMode, 0), Ct);
        Assert.Equal(WhatsAppChatErrors.Forbidden, result.Error);
        Assert.Equal(0, host.Repository.Locks);
        Assert.Equal(0, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_session_is_required_before_mutation()
    {
        var host = new WhatsAppChatModeFixture([WhatsAppPermissions.Send]);
        host.CurrentUser.UserId = null;
        var result = await host.Service.ChangeAsync(new(host.Repository.Contact.Id, WhatsAppConversationMode.Manual, 0), Ct);
        Assert.Equal(WhatsAppChatErrors.SessionRequired, result.Error);
        Assert.Equal(0, host.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task A_stale_revision_cannot_stop_a_different_mode_change()
    {
        var host = new WhatsAppChatModeFixture([WhatsAppPermissions.Send]);
        host.Repository.Contact.ChangeMode(AssistantMode);
        var result = await host.Service.ChangeAsync(new(host.Repository.Contact.Id, WhatsAppConversationMode.Manual, 0), Ct);
        Assert.Equal(WhatsAppChatErrors.RevisionConflict, result.Error);
        Assert.Equal(AssistantMode, host.Repository.Contact.Mode);
    }

    [Fact]
    public async Task Repeating_manual_mode_keeps_the_same_revision_without_consulting_ai()
    {
        var host = new WhatsAppChatModeFixture([WhatsAppPermissions.Send]);
        host.Assistant.State = "notInstalled";
        var result = await host.Service.ChangeAsync(new(host.Repository.Contact.Id, WhatsAppConversationMode.Manual, 0), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ModeRevision);
        Assert.False(result.Value.AssistantSendInProgress);
        Assert.Equal(0, host.Assistant.Reads);
    }
}
