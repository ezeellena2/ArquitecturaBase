using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantRequestTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc);
    private static WhatsAppAssistantRequest Create(WhatsAppAssistantRequestKind kind = WhatsAppAssistantRequestKind.Manual) =>
        WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), kind, 3, 7, "{server-context}", Now, Now.AddHours(1));

    [Fact]
    public void An_automatic_interpretation_continues_one_previous_operation_while_pending()
    {
        var request = Create(WhatsAppAssistantRequestKind.Automatic); var previous = Guid.CreateVersion7();
        Assert.False(request.ContinueFrom(Guid.Empty)); Assert.False(request.ContinueFrom(request.RequestId));
        Assert.True(request.ContinueFrom(previous)); Assert.True(request.ContinueFrom(previous));
        Assert.False(request.ContinueFrom(Guid.CreateVersion7()));
        Assert.Equal(previous, request.PreviousRequestId);
        Assert.False(Create().ContinueFrom(previous));
        var answered = Create(WhatsAppAssistantRequestKind.Automatic); Assert.True(answered.FinishWithoutReply(false, Now));
        Assert.False(answered.ContinueFrom(previous));
    }

    [Fact]
    public void The_reply_to_change_something_names_once_the_operation_the_next_message_changes()
    {
        var reply = Create(WhatsAppAssistantRequestKind.Automatic); var proposal = Guid.CreateVersion7();
        Assert.True(reply.AwaitChange(proposal)); Assert.True(reply.AwaitChange(proposal));
        Assert.False(reply.AwaitChange(Guid.CreateVersion7())); Assert.False(Create().AwaitChange(proposal));
        Assert.Equal(proposal, reply.ChangeRequestId); Assert.Null(reply.PreviousRequestId);
    }

    [Fact]
    public void A_question_about_what_to_build_waits_for_the_answer_that_continues_its_own_operation()
    {
        var question = Create(WhatsAppAssistantRequestKind.Automatic);
        Assert.True(question.AwaitAnswer()); Assert.True(question.AwaitAnswer());
        Assert.Equal(question.RequestId, question.ChangeRequestId);
        Assert.False(Create().AwaitAnswer());
        var change = Create(WhatsAppAssistantRequestKind.Automatic); Assert.True(change.AwaitChange(Guid.CreateVersion7()));
        Assert.False(change.AwaitAnswer());
        var answered = Create(WhatsAppAssistantRequestKind.Automatic); Assert.True(answered.FinishWithoutReply(false, Now));
        Assert.False(answered.AwaitAnswer());
    }

    // IA, Stage 2: the reference names the spreadsheet message its request carries, so a recovery makes the same request.
    [Fact]
    public void A_reference_carries_once_the_spreadsheet_its_request_brings_while_pending()
    {
        var request = Create(WhatsAppAssistantRequestKind.Automatic); var spreadsheet = Guid.CreateVersion7();
        Assert.False(request.CarryAttachment(Guid.Empty));
        Assert.True(request.CarryAttachment(spreadsheet)); Assert.True(request.CarryAttachment(spreadsheet));
        Assert.False(request.CarryAttachment(Guid.CreateVersion7()));
        Assert.Equal(spreadsheet, request.AttachmentMessageId);
        Assert.True(Create().CarryAttachment(spreadsheet));
        var answered = Create(WhatsAppAssistantRequestKind.Automatic); Assert.True(answered.FinishWithoutReply(false, Now));
        Assert.False(answered.CarryAttachment(spreadsheet)); Assert.Null(answered.AttachmentMessageId);
        var captured = Create(WhatsAppAssistantRequestKind.Automatic);
        Assert.True(captured.SetCapturedSuggestion(Guid.CreateVersion7(), null, null, "es", false, Now));
        Assert.False(captured.CarryAttachment(spreadsheet));
        Assert.False(request.SetCapturedSuggestion(Guid.CreateVersion7(), null, null, "es", false, Now));
        Assert.Null(request.SourceRecordId);
    }

    // An interpretation in progress that a newer message replaces did not answer: the next message continues what it continued.
    [Fact]
    public void A_replaced_interpretation_leaves_the_next_message_what_it_continued()
    {
        var replaced = Create(WhatsAppAssistantRequestKind.Automatic); var previous = Guid.CreateVersion7();
        Assert.True(replaced.ContinueFrom(previous));
        Assert.True(replaced.Replace(Now)); Assert.True(replaced.Replace(Now));
        Assert.Equal((WhatsAppAssistantRequestState.Blocked, (Guid?)previous, (DateTime?)Now), (replaced.State, replaced.ChangeRequestId, replaced.DecidedAtUtc));
        var alone = Create(WhatsAppAssistantRequestKind.Automatic);
        Assert.True(alone.Replace(Now));
        Assert.Equal((WhatsAppAssistantRequestState.Blocked, (Guid?)null), (alone.State, alone.ChangeRequestId));
        var answered = Create(WhatsAppAssistantRequestKind.Automatic); Assert.True(answered.ContinueFrom(previous));
        Assert.True(answered.FinishWithoutReply(false, Now));
        Assert.False(answered.Replace(Now)); Assert.Null(answered.ChangeRequestId);
    }

    [Fact]
    public void A_reference_preserves_its_server_identities_without_creating_an_operator()
    {
        var request = Create(WhatsAppAssistantRequestKind.Automatic);
        Assert.Equal(WhatsAppAssistantRequestState.Pending, request.State);
        Assert.Equal(3, request.ModeRevision);
        Assert.Equal(7, request.LinkRevision);
        Assert.Null(request.DecidedByUserId);
        Assert.DoesNotContain("server-context", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_result_can_be_selected_once_and_a_replay_keeps_the_original_selection()
    {
        var request = Create(); var proposal = Guid.CreateVersion7(); var definition = Guid.CreateVersion7();
        Assert.True(request.Ready(proposal, definition, Now));
        Assert.Equal(WhatsAppAssistantRequestState.Ready, request.State);
        Assert.Equal(proposal, request.ProposalId); Assert.Equal(definition, request.DefinitionId);
        Assert.True(request.Ready(proposal, definition, Now.AddMinutes(1)));
        Assert.False(request.Ready(Guid.CreateVersion7(), definition, Now.AddMinutes(1)));
    }

    [Theory]
    [InlineData(false, WhatsAppAssistantRequestState.NoReply)]
    [InlineData(true, WhatsAppAssistantRequestState.Blocked)]
    public void A_non_sending_decision_is_terminal_without_claiming_an_external_effect(bool blocked, WhatsAppAssistantRequestState expected)
    {
        var request = Create(); Assert.True(request.FinishWithoutReply(blocked, Now));
        Assert.Equal(expected, request.State); Assert.Null(request.OutgoingId); Assert.Null(request.DecidedByUserId);
        Assert.True(request.FinishWithoutReply(blocked, Now.AddMinutes(1)));
        Assert.False(request.Ready(Guid.CreateVersion7(), null, Now.AddMinutes(1)));
    }

    [Fact]
    public void Discard_is_durable_and_cannot_later_accept_the_same_proposal()
    {
        var request = Create(); var actor = Guid.CreateVersion7(); request.Ready(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        Assert.True(request.Discard(actor, Now.AddMinutes(1)));
        Assert.Equal(WhatsAppAssistantRequestState.Discarded, request.State);
        Assert.Equal(actor, request.DecidedByUserId); Assert.Equal(Now.AddMinutes(1), request.DecidedAtUtc);
        Assert.True(request.Discard(actor, Now.AddMinutes(2)));
        Assert.Equal(Now.AddMinutes(1), request.DecidedAtUtc);
        Assert.False(request.Accept(Guid.CreateVersion7(), new string('a', 64), actor, Now.AddMinutes(2)));
    }

    [Fact]
    public void The_first_manual_acceptance_attributes_the_real_confirming_operator_and_is_shared()
    {
        var request = Create(); var outgoing = Guid.CreateVersion7(); var actor = Guid.CreateVersion7(); var fingerprint = new string('a', 64);
        request.Ready(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        Assert.True(request.Accept(outgoing, fingerprint, actor, Now.AddMinutes(1)));
        Assert.Equal(WhatsAppAssistantRequestState.Sent, request.State); Assert.Equal(actor, request.DecidedByUserId);
        Assert.Equal(Now.AddMinutes(1), request.DecidedAtUtc);
        Assert.True(request.Accept(outgoing, fingerprint, Guid.CreateVersion7(), Now.AddMinutes(2)));
        Assert.Equal(actor, request.DecidedByUserId); Assert.Equal(Now.AddMinutes(1), request.DecidedAtUtc);
        Assert.False(request.Accept(outgoing, new string('b', 64), actor, Now.AddMinutes(2)));
        Assert.False(request.Discard(actor, Now.AddMinutes(2)));
    }

    [Fact]
    public void An_automatic_acceptance_has_no_operator_and_cannot_become_a_human_send()
    {
        var request = Create(WhatsAppAssistantRequestKind.Automatic); request.Ready(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        Assert.True(request.Accept(Guid.CreateVersion7(), new string('a',64), null, Now));
        Assert.Null(request.DecidedByUserId);
        var other = Create(WhatsAppAssistantRequestKind.Automatic); other.Ready(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        Assert.False(other.Accept(Guid.CreateVersion7(), new string('a',64), Guid.CreateVersion7(), Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Expiry_blocks_a_new_selection_or_acceptance(bool readyFirst)
    {
        var request = Create(); if (readyFirst) request.Ready(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        Assert.False(request.Ready(Guid.CreateVersion7(), null, Now.AddHours(1)));
        Assert.False(request.Accept(Guid.CreateVersion7(), new string('a',64), Guid.CreateVersion7(), Now.AddHours(1)));
    }

    [Fact]
    public void A_pending_reference_cannot_be_discarded_or_sent_before_a_validated_proposal()
    {
        var request = Create();
        Assert.False(request.Discard(Guid.CreateVersion7(), Now));
        Assert.False(request.Accept(Guid.CreateVersion7(), new string('a',64), Guid.CreateVersion7(), Now));
        Assert.Equal(WhatsAppAssistantRequestState.Pending, request.State);
    }

    [Fact]
    public void A_clarification_has_one_durable_question_and_only_one_chosen_definition()
    {
        var request=Create(WhatsAppAssistantRequestKind.Automatic); request.Ready(Guid.NewGuid(),null,Now);
        var question=Guid.NewGuid(); var definition=Guid.NewGuid();
        Assert.True(request.AttachQuestion(question,Now)); Assert.True(request.AttachQuestion(question,Now.AddMinutes(1)));
        Assert.False(request.AttachQuestion(Guid.NewGuid(),Now));
        Assert.True(request.SelectDefinition(definition,Now)); Assert.True(request.SelectDefinition(definition,Now));
        Assert.False(request.SelectDefinition(Guid.NewGuid(),Now)); Assert.Equal(question,request.QuestionOutgoingId); Assert.Equal(definition,request.DefinitionId);
    }
    [Fact]
    public void A_clarification_cannot_choose_before_its_question_or_after_expiry()
    {
        var request=Create(WhatsAppAssistantRequestKind.Automatic); request.Ready(Guid.NewGuid(),null,Now);
        Assert.False(request.SelectDefinition(Guid.NewGuid(),Now)); Assert.False(request.AttachQuestion(Guid.NewGuid(),Now.AddHours(1)));
        var manual=Create(); manual.Ready(Guid.NewGuid(),null,Now); Assert.False(manual.AttachQuestion(Guid.NewGuid(),Now));
    }
    [Fact]
    public void A_validated_decline_is_durable_and_distinct_from_a_manual_dismissal()
    {
        var request=Create(WhatsAppAssistantRequestKind.Automatic); var definition=Guid.NewGuid();
        Assert.True(request.RecordDecline(definition,Now)); Assert.Equal(definition,request.DeclinedDefinitionId);
        Assert.Equal(WhatsAppAssistantRequestState.NoReply,request.State);
        Assert.True(request.RecordDecline(definition,Now.AddMinutes(1))); Assert.Equal(Now,request.DecidedAtUtc);
        Assert.False(request.RecordDecline(Guid.NewGuid(),Now));
        var manual=Create(); manual.Ready(Guid.NewGuid(),definition,Now); manual.Discard(Guid.NewGuid(),Now);
        Assert.Null(manual.DeclinedDefinitionId);
    }
}
