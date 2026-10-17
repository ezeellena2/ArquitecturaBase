using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAssistantServiceTests
{
    [Fact]
    public async Task Manual_request_saves_a_single_reference_and_wakes_only_after_commit_without_executing_the_model()
    {
        var host=new WhatsAppAssistantSuggestionFixture();
        var result=await host.Service.RequestAsync(host.Request,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Equal(WhatsAppAssistantRequestState.Pending,result.Value.State);
        var saved=Assert.Single(host.Requests.Rows); Assert.Equal(host.Context.Operator.Id,saved.ActorUserId); Assert.Equal(host.Context.Subject.Id,saved.SubjectUserId);
        Assert.DoesNotContain("Fictitious visit",saved.ContextJson,StringComparison.Ordinal);
        Assert.Equal(1,host.UnitOfWork.Transactions);Assert.Equal(1,host.UnitOfWork.Commits); Assert.Equal(1,host.Wake.Notifications); Assert.Equal(0,host.Assistant.Submits);
    }
    [Fact]
    public async Task Remount_or_a_second_operator_reuses_the_same_source_reference_without_another_model_request()
    {
        var host=new WhatsAppAssistantSuggestionFixture();
        var first=await host.Service.RequestAsync(host.Request,TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess);
        var other=host.Context.Users.AddUser("other-assistant-operator@example.test");
        host.Context.Permissions.Permissions[other.Id]=[WhatsAppPermissions.Read,WhatsAppPermissions.Send]; host.Context.Current.UserId=other.Id;
        var replay=await host.Service.RequestAsync(host.Request with{RequestId=Guid.NewGuid()},TestContext.Current.CancellationToken);
        Assert.True(replay.IsSuccess); Assert.Equal(first.Value.RequestId,replay.Value.RequestId); Assert.Single(host.Requests.Rows);
        Assert.Equal(host.Context.Operator.Id,host.Requests.Rows[0].ActorUserId);Assert.Equal(0,host.Assistant.Submits);
    }
    [Theory]
    [InlineData(false,ErrorType.Unauthorized)]
    [InlineData(true,ErrorType.Forbidden)]
    public async Task No_session_or_send_permission_is_rejected_before_opening_a_transaction(bool hasSession,ErrorType expected)
    {
        var host=new WhatsAppAssistantSuggestionFixture();
        if(!hasSession)host.Context.Current.UserId=null;else host.Context.Permissions.Permissions[host.Context.Operator.Id]=[WhatsAppPermissions.Read];
        var result=await host.Service.RequestAsync(host.Request,TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(expected,result.Error.Type);Assert.Equal(0,host.UnitOfWork.Transactions);Assert.Empty(host.Requests.Rows);
    }
    [Fact]
    public async Task A_stale_mode_revision_does_not_capture_an_operation()
    {
        var host=new WhatsAppAssistantSuggestionFixture();var result=await host.Service.RequestAsync(host.Request with{ExpectedModeRevision=99},TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(WhatsAppAssistantErrors.StateConflict.Code,result.Error.Code);Assert.Empty(host.Requests.Rows);
    }
    [Fact]
    public async Task An_old_source_cannot_replace_the_latest_inbound_business_text()
    {
        var host=new WhatsAppAssistantSuggestionFixture();var result=await host.Service.RequestAsync(host.Request with{SourceMessageId=Guid.NewGuid()},TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(WhatsAppAssistantErrors.StateConflict.Code,result.Error.Code);Assert.Empty(host.Requests.Rows);
    }
    [Fact]
    public async Task Manual_suggestion_requests_do_not_change_assistant_mode_or_send_on_behalf_of_an_operator()
    {
        var host=new WhatsAppAssistantSuggestionFixture();host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var result=await host.Service.RequestAsync(host.Request,TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Equal(WhatsAppAssistantErrors.StateConflict.Code,result.Error.Code);Assert.Empty(host.Requests.Rows);Assert.Empty(host.Conversation.Outgoing);
    }
    [Fact]
    public async Task A_locally_paused_connection_does_not_accept_a_new_suggestion()
    {
        var host=new WhatsAppAssistantSuggestionFixture();host.Assistant.Availability="paused";
        var result=await host.Service.RequestAsync(host.Request,TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Empty(host.Requests.Rows);Assert.Equal(0,host.Assistant.Submits);
    }
    [Fact]
    public async Task Consulting_a_pending_reference_is_read_only_and_never_submits_the_model()
    {
        var host=new WhatsAppAssistantSuggestionFixture(); var created=await host.Service.RequestAsync(host.Request,TestContext.Current.CancellationToken);Assert.True(created.IsSuccess);
        var transactions=host.UnitOfWork.Transactions;
        var result=await host.Service.GetAsync(host.Request.ContactId,created.Value.RequestId,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Pending,result.Value.State);Assert.Null(result.Value.Proposal);
        Assert.Equal(transactions,host.UnitOfWork.Transactions);Assert.Equal(0,host.Assistant.Submits);
    }
}
