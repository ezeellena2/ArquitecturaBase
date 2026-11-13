using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// DOC6 (task 14): «Armar formulario con la IA» from one unclassified document or a group of similar ones: what was asked and with which
// context, the documents it was built from, the draft it created once and the version its documents were read again for.
public sealed class FormDocumentProposalTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_request_keeps_who_asked_in_which_language_and_zone_and_its_documents_in_order()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var topic = Guid.NewGuid();

        var requested = FormDocumentProposal.Request(Guid.NewGuid(), Guid.NewGuid(), "es", "America/Argentina/Buenos_Aires", topic, [first, second], Now);

        Assert.True(requested.IsSuccess);
        var proposal = requested.Value;
        Assert.Equal([first, second], proposal.DocumentIds);
        Assert.Equal((topic, "es", "America/Argentina/Buenos_Aires", Now), (proposal.TopicId!.Value, proposal.Culture, proposal.TimeZoneId, proposal.RequestedAtUtc));
        Assert.Null(proposal.DefinitionId);
        Assert.Null(proposal.RereadVersionId);
    }

    [Fact]
    public void A_request_needs_its_ids_a_known_language_a_zone_a_utc_moment_and_between_one_and_twenty_distinct_documents()
    {
        var actor = Guid.NewGuid();
        Guid[] one = [Guid.NewGuid()];
        Assert.True(FormDocumentProposal.Request(Guid.Empty, actor, "es", "UTC", null, one, Now).IsFailure);
        Assert.True(FormDocumentProposal.Request(Guid.NewGuid(), Guid.Empty, "es", "UTC", null, one, Now).IsFailure);
        Assert.True(FormDocumentProposal.Request(Guid.NewGuid(), actor, "pt", "UTC", null, one, Now).IsFailure);
        Assert.True(FormDocumentProposal.Request(Guid.NewGuid(), actor, "es", " ", null, one, Now).IsFailure);
        Assert.True(FormDocumentProposal.Request(Guid.NewGuid(), actor, "es", "UTC", null, one, DateTime.SpecifyKind(Now, DateTimeKind.Local)).IsFailure);
        Assert.True(FormDocumentProposal.Request(Guid.NewGuid(), actor, "es", "UTC", Guid.Empty, one, Now).IsFailure);
        Assert.True(FormDocumentProposal.Request(Guid.NewGuid(), actor, "es", "UTC", null, [], Now).IsFailure);
        Assert.True(FormDocumentProposal.Request(Guid.NewGuid(), actor, "es", "UTC", null, [Guid.Empty], Now).IsFailure);
        Assert.True(FormDocumentProposal.Request(Guid.NewGuid(), actor, "es", "UTC", null, [one[0], one[0]], Now).IsFailure);
        var twenty = Enumerable.Range(0, FormDocumentProposal.MaxDocuments).Select(_ => Guid.NewGuid()).ToArray();
        Assert.True(FormDocumentProposal.Request(Guid.NewGuid(), actor, "en", "UTC", null, twenty, Now).IsSuccess);
        Assert.True(FormDocumentProposal.Request(Guid.NewGuid(), actor, "en", "UTC", null, [.. twenty, Guid.NewGuid()], Now).IsFailure);
    }

    [Fact]
    public void The_same_request_again_is_the_same_document_or_the_same_group_and_anything_else_is_another_one()
    {
        var document = Guid.NewGuid();
        var topic = Guid.NewGuid();
        var single = FormDocumentProposal.Request(Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", null, [document], Now).Value;
        var group = FormDocumentProposal.Request(Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", topic, [document, Guid.NewGuid()], Now).Value;

        Assert.True(single.Asks(document, null));
        Assert.False(single.Asks(Guid.NewGuid(), null));
        Assert.False(single.Asks(null, topic));
        // A group is asked by its topic: its documents are the ones the server chose when it was asked.
        Assert.True(group.Asks(null, topic));
        Assert.False(group.Asks(null, Guid.NewGuid()));
        Assert.False(group.Asks(document, null));
    }

    [Fact]
    public void Its_draft_and_the_proposal_it_came_from_are_recorded_once_and_the_same_again_changes_nothing()
    {
        var proposal = FormDocumentProposal.Request(Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", null, [Guid.NewGuid()], Now).Value;
        var draft = Guid.NewGuid();
        var source = Guid.NewGuid();

        Assert.True(proposal.Draft(draft, source).IsSuccess);
        Assert.True(proposal.Draft(draft, source).IsSuccess);
        Assert.Equal(FormReceivedDocumentErrors.RequestConflict, proposal.Draft(Guid.NewGuid(), source).Error);
        Assert.Equal(FormReceivedDocumentErrors.RequestConflict, proposal.Draft(draft, Guid.NewGuid()).Error);
        Assert.Equal(FormReceivedDocumentErrors.ContentInvalid, proposal.Draft(Guid.Empty, source).Error);
        Assert.Equal(FormReceivedDocumentErrors.ContentInvalid, proposal.Draft(draft, Guid.Empty).Error);
        Assert.Equal((draft, source), (proposal.DefinitionId!.Value, proposal.ProposalId!.Value));
    }

    [Fact]
    public void Its_documents_are_read_again_once_per_published_version_and_only_after_its_draft()
    {
        Guid[] documents = [Guid.NewGuid(), Guid.NewGuid()];
        var proposal = FormDocumentProposal.Request(Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", null, documents, Now).Value;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var hold = Now.AddMinutes(2);

        Assert.Equal(FormReceivedDocumentErrors.StateConflict, proposal.StartReread(first, Now, hold).Error);
        Assert.True(proposal.Draft(Guid.NewGuid(), Guid.NewGuid()).IsSuccess);
        Assert.False(proposal.RereadsFor(first));

        Assert.True(proposal.StartReread(first, Now, hold).IsSuccess);
        Assert.True(proposal.RereadsFor(first));
        // All of them wait, held by the round that starts.
        Assert.Equal(documents, proposal.PendingRereadIds);
        Assert.Equal(hold, proposal.RereadAfterUtc);
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, proposal.StartReread(first, Now, hold).Error);
        // A later publication, once nothing holds them, reads them all again for its own version.
        proposal.Reread(documents[0]);
        Assert.True(proposal.StartReread(second, hold, hold.AddMinutes(2)).IsSuccess);
        Assert.Equal(second, proposal.RereadVersionId);
        Assert.Equal(documents, proposal.PendingRereadIds);
        Assert.Equal(FormReceivedDocumentErrors.ContentInvalid, proposal.StartReread(Guid.Empty, Now, hold).Error);
        Assert.Equal(FormReceivedDocumentErrors.ContentInvalid, proposal.StartReread(Guid.NewGuid(), Now, DateTime.SpecifyKind(hold, DateTimeKind.Local)).Error);
        Assert.Equal(FormReceivedDocumentErrors.ContentInvalid, proposal.StartReread(Guid.NewGuid(), hold.AddMinutes(4), hold.AddMinutes(4)).Error);
    }

    [Fact]
    public void A_newer_version_starts_over_only_once_nothing_holds_the_pending_ones()
    {
        Guid[] documents = [Guid.NewGuid(), Guid.NewGuid()];
        var proposal = FormDocumentProposal.Request(Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", null, documents, Now).Value;
        Assert.True(proposal.Draft(Guid.NewGuid(), Guid.NewGuid()).IsSuccess);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Assert.True(proposal.StartReread(first, Now, Now.AddMinutes(2)).IsSuccess);
        proposal.Reread(documents[0]);

        // Held by the round that took them, or by an engine that cannot take them now: a newer version waits, so two rounds never run at once.
        Assert.True(proposal.RereadHeld(Now.AddMinutes(1)));
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, proposal.StartReread(second, Now.AddMinutes(1), Now.AddMinutes(3)).Error);
        Assert.True(proposal.DeferReread(Now.AddHours(1)).IsSuccess);
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, proposal.StartReread(second, Now.AddMinutes(30), Now.AddMinutes(32)).Error);
        Assert.Equal(first, proposal.RereadVersionId);
        Assert.Equal([documents[1]], proposal.PendingRereadIds);
        // Once nothing holds them, it starts over with all of them.
        Assert.False(proposal.RereadHeld(Now.AddHours(1)));
        Assert.True(proposal.StartReread(second, Now.AddHours(1), Now.AddHours(1).AddMinutes(2)).IsSuccess);
        Assert.Equal(second, proposal.RereadVersionId);
        Assert.Equal(documents, proposal.PendingRereadIds);
    }

    [Fact]
    public void The_re_reads_it_took_are_counted_per_utc_day()
    {
        var proposal = FormDocumentProposal.Request(Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", null, [Guid.NewGuid()], Now).Value;
        var day = DateOnly.FromDateTime(Now);

        Assert.Equal(0, proposal.RereadsOn(day));
        Assert.True(proposal.CountRereads(day, 3).IsSuccess);
        Assert.True(proposal.CountRereads(day, 2).IsSuccess);
        Assert.Equal(5, proposal.RereadsOn(day));
        // A new day starts from none.
        Assert.Equal(0, proposal.RereadsOn(day.AddDays(1)));
        Assert.True(proposal.CountRereads(day.AddDays(1), 1).IsSuccess);
        Assert.Equal((1, 0), (proposal.RereadsOn(day.AddDays(1)), proposal.RereadsOn(day)));
        Assert.Equal(FormReceivedDocumentErrors.ContentInvalid, proposal.CountRereads(day, -1).Error);
    }

    [Fact]
    public void The_documents_to_read_again_wait_for_their_round_leave_one_by_one_and_wait_for_the_engine_when_it_cannot_take_them()
    {
        Guid[] documents = [Guid.NewGuid(), Guid.NewGuid()];
        var proposal = FormDocumentProposal.Request(Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", null, documents, Now).Value;
        Assert.True(proposal.Draft(Guid.NewGuid(), Guid.NewGuid()).IsSuccess);
        Assert.True(proposal.StartReread(Guid.NewGuid(), Now, Now.AddMinutes(2)).IsSuccess);

        // Held by the round that started: another round takes them only once the hold passes, and holds them in turn.
        Assert.False(proposal.RereadDue(Now.AddMinutes(1)));
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, proposal.ClaimReread(Now.AddMinutes(1), Now.AddMinutes(3)).Error);
        Assert.True(proposal.RereadDue(Now.AddMinutes(2)));
        Assert.True(proposal.ClaimReread(Now.AddMinutes(2), Now.AddMinutes(4)).IsSuccess);
        Assert.Equal(Now.AddMinutes(4), proposal.RereadAfterUtc);
        // The engine cannot take them now: they wait until it says.
        Assert.True(proposal.DeferReread(Now.AddHours(10)).IsSuccess);
        Assert.Equal(Now.AddHours(10), proposal.RereadAfterUtc);
        // Each one read again leaves; with the last one nothing waits.
        proposal.Reread(documents[0]);
        Assert.Equal([documents[1]], proposal.PendingRereadIds);
        proposal.Reread(documents[1]);
        Assert.Empty(proposal.PendingRereadIds);
        Assert.Null(proposal.RereadAfterUtc);
        Assert.False(proposal.RereadDue(Now.AddDays(1)));
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, proposal.ClaimReread(Now.AddDays(1), Now.AddDays(2)).Error);
    }
}
