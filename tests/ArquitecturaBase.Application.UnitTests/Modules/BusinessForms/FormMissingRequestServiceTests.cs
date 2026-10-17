using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMissingRequestServiceTests
{
    [Fact]
    public async Task A_clear_missing_intent_records_one_generalized_topic_without_response_permission()
    {
        var f = new FormMissingRequestFixture(); var result = await f.Service.CaptureAsync(f.Request(),Ct);
        Assert.True(result.IsSuccess); Assert.False(result.Value.WasAlreadyRecorded);
        Assert.Equal(Assert.Single(f.Topics).Id,Assert.Single(f.Requests).TopicId);
        Assert.Equal(1,f.Uow.Transactions); Assert.Equal(1,f.Uow.Commits); Assert.Equal(1,f.Locks);
    }
    [Fact]
    public async Task Reinterpreting_the_same_inbound_does_not_count_a_second_request()
    {
        var f = new FormMissingRequestFixture(); var request = f.Request();
        var first = await f.Service.CaptureAsync(request,Ct); Assert.True(first.IsSuccess);
        var replay = await f.Service.CaptureAsync(f.Request(request.SourceReceiptId),Ct);
        Assert.True(replay.IsSuccess); Assert.True(replay.Value.WasAlreadyRecorded);
        Assert.Equal(first.Value.RequestId,replay.Value.RequestId); Assert.Single(f.Requests); Assert.Equal(1,Assert.Single(f.Topics).Revision);
    }
    [Fact]
    public async Task Different_messages_from_one_person_count_requests_separately_and_people_once()
    {
        var f = new FormMissingRequestFixture(); Assert.True((await f.Service.CaptureAsync(f.Request(),Ct)).IsSuccess);
        Assert.True((await f.Service.CaptureAsync(f.Request(),Ct)).IsSuccess);
        var page = await f.Service.ListAsync(new(),Ct); Assert.True(page.IsSuccess);
        var row = Assert.Single(page.Value.Items); Assert.Equal(2,row.RequestCount); Assert.Equal(1,row.PersonCount);
        Assert.Equal(2,row.Revision); Assert.Equal(2,page.Value.TotalRequests);
    }
    [Fact]
    public async Task Different_people_join_one_observed_generalized_topic()
    {
        var f = new FormMissingRequestFixture(); Assert.True((await f.Service.CaptureAsync(f.Request(),Ct)).IsSuccess);
        var other = f.Assistant.Users.AddUser("other@example.invalid").Id; var topic = Assert.Single(f.Topics);
        Assert.True((await f.Service.CaptureAsync(f.Request(subject:other,existing:topic),Ct)).IsSuccess);
        var page = await f.Service.ListAsync(new(),Ct); Assert.Equal(2,Assert.Single(page.Value.Items).PersonCount);
    }
    [Fact]
    public async Task A_new_request_reopens_a_discarded_topic_and_preserves_the_discard_fact()
    {
        var f = new FormMissingRequestFixture(); Assert.True((await f.Service.CaptureAsync(f.Request(),Ct)).IsSuccess);
        var topic = Assert.Single(f.Topics); var discarded = await f.Service.DiscardAsync(topic.Id,new(topic.Revision),Ct);
        Assert.True(discarded.IsSuccess); var time = topic.DiscardedAtUtc;
        Assert.True((await f.Service.CaptureAsync(f.Request(existing:topic),Ct)).IsSuccess);
        Assert.Equal(BusinessFormMissingTopicState.Pending,topic.State); Assert.Equal(time,topic.DiscardedAtUtc); Assert.Equal(f.Manager,topic.DiscardedByUserId);
    }
    [Fact]
    public async Task A_replay_after_discard_does_not_reopen_the_topic()
    {
        var f = new FormMissingRequestFixture(); var request = f.Request(); Assert.True((await f.Service.CaptureAsync(request,Ct)).IsSuccess);
        var topic = Assert.Single(f.Topics); Assert.True((await f.Service.DiscardAsync(topic.Id,new(topic.Revision),Ct)).IsSuccess);
        var revision = topic.Revision; Assert.True((await f.Service.CaptureAsync(request,Ct)).IsSuccess);
        Assert.Equal(BusinessFormMissingTopicState.Discarded,topic.State); Assert.Equal(revision,topic.Revision);
    }
    [Fact]
    public async Task Stale_discard_returns_conflict_without_overwriting_the_actual_decision()
    {
        var f = new FormMissingRequestFixture(); Assert.True((await f.Service.CaptureAsync(f.Request(),Ct)).IsSuccess);
        var topic = Assert.Single(f.Topics); Assert.True((await f.Service.DiscardAsync(topic.Id,new(topic.Revision),Ct)).IsSuccess);
        var result = await f.Service.DiscardAsync(topic.Id,new(1),Ct);
        Assert.True(result.IsFailure); Assert.Equal(FormMissingRequestErrors.RevisionConflict.Code,result.Error.Code);
    }
    [Fact]
    public async Task The_board_and_discard_require_manage_without_requiring_forms_or_records_read()
    {
        var f = new FormMissingRequestFixture(); Assert.True((await f.Service.CaptureAsync(f.Request(),Ct)).IsSuccess);
        Assert.True((await f.Service.ListAsync(new(),Ct)).IsSuccess);
        var topic = Assert.Single(f.Topics); Assert.True((await f.Service.DiscardAsync(topic.Id,new(1),Ct)).IsSuccess);
        f.Assistant.Permissions.Permissions[f.Manager] = [BusinessFormsPermissions.Read,BusinessFormsPermissions.RecordsRead];
        Assert.True((await f.Service.ListAsync(new(),Ct)).IsFailure);
    }
    [Fact]
    public async Task Revoked_manage_is_rechecked_under_the_mutation_lock()
    {
        var f = new FormMissingRequestFixture(); Assert.True((await f.Service.CaptureAsync(f.Request(),Ct)).IsSuccess);
        var topic = Assert.Single(f.Topics); f.OnLock = () => f.Assistant.Permissions.Permissions[f.Manager] = [];
        Assert.True((await f.Service.DiscardAsync(topic.Id,new(1),Ct)).IsFailure); Assert.Equal(BusinessFormMissingTopicState.Pending,topic.State);
    }
    [Fact]
    public async Task Revoked_manage_after_a_read_does_not_return_the_board()
    {
        var f = new FormMissingRequestFixture(); f.OnRead = () => f.Assistant.Permissions.Permissions[f.Manager] = [];
        Assert.True((await f.Service.ListAsync(new(),Ct)).IsFailure);
    }
    [Theory]
    [InlineData("subject")]
    [InlineData("channel")]
    [InlineData("provenance")]
    [InlineData("receipt")]
    [InlineData("observation")]
    public async Task Invalid_capture_authority_is_rejected_without_a_transaction(string attack)
    {
        var f = new FormMissingRequestFixture(); var request = f.Request();
        if (attack == "subject") request = request with { Context = request.Context with { SubjectUserId = Guid.CreateVersion7() } };
        if (attack == "channel") request = request with { Context = request.Context with { ChannelId = "admin" } };
        if (attack == "provenance") request = request with { Provenance = request.Provenance with { ChannelId = "other" } };
        if (attack == "receipt") request = request with { SourceReceiptId = Guid.Empty };
        if (attack == "observation") request = request with { Proposal = request.Proposal with { ObservedReads = [] } };
        Assert.True((await f.Service.CaptureAsync(request,Ct)).IsFailure); Assert.Equal(0,f.Uow.Transactions); Assert.Empty(f.Requests);
    }
    [Fact]
    public async Task A_receipt_cannot_be_reused_for_a_different_person()
    {
        var f = new FormMissingRequestFixture(); var request = f.Request(); Assert.True((await f.Service.CaptureAsync(request,Ct)).IsSuccess);
        var other = f.Assistant.Users.AddUser("other@example.invalid").Id;
        Assert.True((await f.Service.CaptureAsync(f.Request(request.SourceReceiptId,other),Ct)).IsFailure); Assert.Single(f.Requests);
    }
    [Fact]
    public async Task Manual_analysis_records_the_same_fact_with_the_current_real_operator()
    {
        var f = new FormMissingRequestFixture(); var request = f.Request();
        request = request with { Context = request.Context with { ChannelId = "whatsapp-manual", ActorUserId = f.Manager },
            Provenance = request.Provenance with { ChannelId = "whatsapp-manual" } };
        Assert.True((await f.Service.CaptureAsync(request,Ct)).IsSuccess); Assert.Single(f.Requests);
    }
    [Fact]
    public async Task Known_personal_identifiers_cannot_become_topic_metadata()
    {
        var f = new FormMissingRequestFixture(); var request = f.Request();
        var payload = Assert.IsType<MissingFormRequestProposalPayload>(request.Proposal.Payload);
        request = request with { Proposal = request.Proposal with { Payload = payload with { Name = "Visita de recipient@example.invalid" } } };
        Assert.True((await f.Service.CaptureAsync(request,Ct)).IsFailure); Assert.Empty(f.Topics);
    }
    [Fact]
    public async Task Paging_and_revision_inputs_are_validated_before_read_or_write()
    {
        var f = new FormMissingRequestFixture(); Assert.True((await f.Service.ListAsync(new() { Page = 0 },Ct)).IsFailure);
        Assert.True((await f.Service.DiscardAsync(Guid.CreateVersion7(),new(null),Ct)).IsFailure); Assert.Equal(0,f.Uow.Transactions);
    }
    [Fact]
    public async Task The_company_calendar_timezone_does_not_require_the_requester_profile_timezone()
    {
        var f = new FormMissingRequestFixture(); var request = f.Request();
        request = request with { Context = request.Context with { TimeZoneId = "UTC" } };
        Assert.True((await f.Service.CaptureAsync(request,Ct)).IsSuccess);
    }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
