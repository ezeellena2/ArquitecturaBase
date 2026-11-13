using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// DOC6 (task 14), after publishing: a request whose draft has an active version published in a channel with its reading on is reserved for
// that version once, and each of its documents still unclassified is read again for the form, one interpretation of forms-documents each,
// with the context of whoever asked for the form; the server applies it when it ends.
public sealed class FormDocumentRereadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_published_version_with_its_reading_on_is_reserved_once_with_the_context_of_whoever_asked()
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe", required: true)]);
        var request = f.Request([f.Document().Id, f.Document().Id], Guid.NewGuid());
        Assert.True(request.Draft(form.DefinitionId, Guid.NewGuid()).IsSuccess);
        var now = f.Inbox.Clock.GetUtcNow().UtcDateTime;

        var reserved = await f.Rereads().ReserveAsync(request.Id, Ct);
        var again = await f.Rereads().ReserveAsync(request.Id, Ct);

        var work = reserved.Value!;
        Assert.Equal((request.Id, form.DefinitionId, form.VersionId, f.Reviewer, "es", request.TimeZoneId),
            (work.ProposalId, work.DefinitionId, work.VersionId, work.ActorUserId, work.Culture, work.TimeZoneId));
        Assert.Equal(request.DocumentIds, work.DocumentIds);
        Assert.Equal(form.VersionId, request.RereadVersionId);
        // All of them wait, held by this round: another host, or a later round, leaves them alone meanwhile, under the same lock.
        Assert.Equal(request.DocumentIds, request.PendingRereadIds);
        Assert.Equal(now.Add(FormDocumentRereadService.Hold), request.RereadAfterUtc);
        Assert.True(again.IsSuccess);
        Assert.Null(again.Value);
        Assert.Equal(2, f.Locks.Count(taken => taken == ("request", request.Id)));
    }

    [Fact]
    public async Task A_reservation_takes_the_person_s_lock_before_the_request_s_and_recording_only_the_request_s()
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe", required: true)]);
        var request = f.Request([f.Document().Id], Guid.NewGuid());
        Assert.True(request.Draft(form.DefinitionId, Guid.NewGuid()).IsSuccess);

        var work = (await f.Rereads().ReserveAsync(request.Id, Ct)).Value!;
        await f.Rereads().RecordAsync(new(request.Id, work.VersionId, work.DocumentIds[0], null), Ct);

        // Always the person's lock (their share, one round at a time) before the request's: two hosts never wait on each other in a cycle.
        Assert.Equal([("asker", f.Reviewer), ("request", request.Id), ("request", request.Id)], f.Locks);
    }

    [Fact]
    public async Task A_newer_version_published_while_a_round_holds_the_pending_ones_starts_over_only_once_that_hold_passes()
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe", required: true)]);
        Guid[] documents = [f.Document().Id, f.Document().Id];
        var request = f.Request(documents, Guid.NewGuid());
        Assert.True(request.Draft(form.DefinitionId, Guid.NewGuid()).IsSuccess);
        var first = (await f.Rereads().ReserveAsync(request.Id, Ct)).Value!;
        await f.Rereads().RecordAsync(new(request.Id, first.VersionId, documents[0], null), Ct);
        var newer = form with { VersionId = Guid.NewGuid() };
        f.Inbox.Forms[f.Inbox.Forms.IndexOf(form)] = newer;

        // Another host would run a second round of the same request meanwhile, and the share would no longer be one round's.
        var meanwhile = await f.Rereads().ReserveAsync(request.Id, Ct);
        Assert.Null(meanwhile.Value);
        Assert.Equal(first.VersionId, request.RereadVersionId);
        Assert.Equal([documents[1]], request.PendingRereadIds);
        f.Inbox.Clock.Advance(FormDocumentRereadService.Hold);
        var second = (await f.Rereads().ReserveAsync(request.Id, Ct)).Value!;

        Assert.Equal(newer.VersionId, second.VersionId);
        Assert.Equal(documents, second.DocumentIds);
    }

    [Fact]
    public async Task A_re_read_the_engine_took_counts_even_once_a_newer_version_started_over_and_that_round_stops()
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe", required: true)]);
        Guid[] documents = [f.Document().Id, f.Document().Id];
        var request = f.Request(documents, Guid.NewGuid());
        Assert.True(request.Draft(form.DefinitionId, Guid.NewGuid()).IsSuccess);
        var work = (await f.Rereads().ReserveAsync(request.Id, Ct)).Value!;
        var today = DateOnly.FromDateTime(f.Inbox.Clock.GetUtcNow().UtcDateTime);
        var older = Guid.NewGuid();

        // A round of an older version (it outlived its hold and another host started over): what the engine took for it still uses the
        // asker's share and a failure does not; the round stops, and the newer version's pending ones stay as they are.
        var took = await f.Rereads().RecordAsync(new(request.Id, older, documents[0], null), Ct);
        var failed = await f.Rereads().RecordAsync(new(request.Id, older, documents[1], FormReceivedDocumentErrors.StateConflict), Ct);

        Assert.False(took.Value);
        Assert.False(failed.Value);
        Assert.Equal(1, request.RereadsOn(today));
        Assert.Equal(work.VersionId, request.RereadVersionId);
        Assert.Equal(documents, request.PendingRereadIds);
    }

    [Fact]
    public async Task What_became_of_each_document_takes_it_off_or_holds_the_rest_until_the_engine_says_and_a_later_round_continues()
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe", required: true)]);
        Guid[] documents = [f.Document().Id, f.Document().Id, f.Document().Id];
        var request = f.Request(documents, Guid.NewGuid());
        Assert.True(request.Draft(form.DefinitionId, Guid.NewGuid()).IsSuccess);
        var work = (await f.Rereads().ReserveAsync(request.Id, Ct)).Value!;
        var now = f.Inbox.Clock.GetUtcNow().UtcDateTime;
        var quota = Error.TooManyRequests("Ai.Assistant.QuotaExceeded", "The acceptance quota is exhausted.", new Dictionary<string, object?> { ["retryAfter"] = 3600 });

        // Read again, or changed meanwhile: off the pending ones, and the round goes on.
        Assert.True((await f.Rereads().RecordAsync(new(request.Id, work.VersionId, documents[0], null), Ct)).Value);
        Assert.True((await f.Rereads().RecordAsync(new(request.Id, work.VersionId, documents[1], FormReceivedDocumentErrors.StateConflict), Ct)).Value);
        // The engine's quota: the one left waits as long as it says, and the round stops.
        Assert.False((await f.Rereads().RecordAsync(new(request.Id, work.VersionId, documents[2], quota), Ct)).Value);

        Assert.Equal([documents[2]], request.PendingRereadIds);
        Assert.Equal(now.AddSeconds(3600), request.RereadAfterUtc);
        Assert.Equal(1, request.RereadsOn(DateOnly.FromDateTime(now)));
        Assert.Null((await f.Rereads().ReserveAsync(request.Id, Ct)).Value);
        f.Inbox.Clock.Advance(TimeSpan.FromSeconds(3600));
        var next = (await f.Rereads().ReserveAsync(request.Id, Ct)).Value!;
        Assert.Equal([documents[2]], next.DocumentIds);
        Assert.Equal(form.VersionId, next.VersionId);
    }

    [Fact]
    public async Task An_asker_s_re_reads_take_at_most_their_daily_share_across_their_requests_and_the_rest_continue_the_next_utc_day()
    {
        var f = new FormDocumentProposalFixture { RereadsPerDay = 3 };
        var question = FormReceivedDocumentFixture.Question("decimal", "Importe", required: true);
        var tickets = f.Inbox.AddForm([question]);
        var invoices = f.Inbox.AddForm([question]);
        var first = f.Request([f.Document().Id, f.Document().Id], Guid.NewGuid());
        var second = f.Request([f.Document().Id, f.Document().Id, f.Document().Id], Guid.NewGuid());
        Assert.True(first.Draft(tickets.DefinitionId, Guid.NewGuid()).IsSuccess);
        Assert.True(second.Draft(invoices.DefinitionId, Guid.NewGuid()).IsSuccess);
        var now = f.Inbox.Clock.GetUtcNow().UtcDateTime;

        var a = (await f.Rereads().ReserveAsync(first.Id, Ct)).Value!;
        // One round of this asker at a time: the second waits while the first holds its documents.
        Assert.Null((await f.Rereads().ReserveAsync(second.Id, Ct)).Value);
        Assert.Null(second.RereadVersionId);
        foreach (var documentId in a.DocumentIds) await f.Rereads().RecordAsync(new(first.Id, a.VersionId, documentId, null), Ct);
        var b = (await f.Rereads().ReserveAsync(second.Id, Ct)).Value!;
        // Each one the engine took uses the share.
        await f.Rereads().RecordAsync(new(second.Id, b.VersionId, b.DocumentIds[0], null), Ct);

        // Three a day for this asker, across both requests: two for the first, one for the second, under the asker's lock.
        Assert.Equal(first.DocumentIds, a.DocumentIds);
        Assert.Equal([second.DocumentIds[0]], b.DocumentIds);
        Assert.Equal(3, f.Locks.Count(taken => taken == ("asker", f.Reviewer)));
        Assert.Equal((2, 1), (first.RereadsOn(DateOnly.FromDateTime(now)), second.RereadsOn(DateOnly.FromDateTime(now))));
        // Once the round's hold passes, the second's other two wait for the next UTC day...
        f.Inbox.Clock.Advance(FormDocumentRereadService.Hold);
        Assert.Null((await f.Rereads().ReserveAsync(second.Id, Ct)).Value);
        var tomorrow = DateOnly.FromDateTime(now).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        Assert.Equal(tomorrow, second.RereadAfterUtc);
        Assert.Equal(second.DocumentIds.Skip(1), second.PendingRereadIds);
        // ...and then continue.
        f.Inbox.Clock.Advance(tomorrow - f.Inbox.Clock.GetUtcNow().UtcDateTime);
        var c = (await f.Rereads().ReserveAsync(second.Id, Ct)).Value!;
        Assert.Equal(second.DocumentIds.Skip(1), c.DocumentIds);
    }

    [Fact]
    public async Task A_transient_refusal_without_a_time_waits_a_minute_and_one_that_will_not_pass_leaves_the_document()
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe", required: true)]);
        Guid[] documents = [f.Document().Id, f.Document().Id];
        var request = f.Request(documents, Guid.NewGuid());
        Assert.True(request.Draft(form.DefinitionId, Guid.NewGuid()).IsSuccess);
        var work = (await f.Rereads().ReserveAsync(request.Id, Ct)).Value!;
        var now = f.Inbox.Clock.GetUtcNow().UtcDateTime;
        var busy = Error.Conflict("Ai.Assistant.ActiveTurn", "An assistant operation is already active.", new Dictionary<string, object?> { ["retryable"] = true });

        Assert.True((await f.Rereads().RecordAsync(new(request.Id, work.VersionId, documents[0], FormDefinitionErrors.Forbidden), Ct)).Value);
        Assert.False((await f.Rereads().RecordAsync(new(request.Id, work.VersionId, documents[1], busy), Ct)).Value);

        Assert.Equal([documents[1]], request.PendingRereadIds);
        Assert.Equal(now.Add(FormDocumentRereadService.RetryDelay), request.RereadAfterUtc);
    }

    [Theory]
    [InlineData("no-draft")]
    [InlineData("reading-off")]
    [InlineData("app-only")]
    [InlineData("not-published")]
    public async Task A_request_without_a_draft_or_whose_form_does_not_read_documents_in_a_channel_is_not_read_again(string kind)
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe")], reading: kind != "reading-off", appOnly: kind == "app-only");
        var request = f.Request([f.Document().Id]);
        if (kind != "no-draft") Assert.True(request.Draft(kind == "not-published" ? Guid.NewGuid() : form.DefinitionId, Guid.NewGuid()).IsSuccess);

        var reserved = await f.Rereads().ReserveAsync(request.Id, Ct);

        Assert.True(reserved.IsSuccess);
        Assert.Null(reserved.Value);
        Assert.Null(request.RereadVersionId);
        Assert.Equal(0, f.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task Each_document_is_read_again_for_the_form_with_the_asker_s_context_in_its_own_conversation()
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe", required: true)], name: "Tickets");
        var document = f.Document();
        var asker = f.Users.AddUser("pidio@example.test", culture: "en").Id;
        f.Permissions.Permissions[asker] = FormDocumentProposalFixture.Designer;

        var read = await f.Extractions().RereadAsync(new(document.Id, form.DefinitionId, asker, "en", "UTC"), Ct);

        Assert.Equal("pending", read.Value.State);
        var operation = Assert.Single(f.Engine.Submitted);
        Assert.Equal(("forms-documents", asker, (Guid?)null, form.DefinitionId, document.Id, "en", "UTC"), (operation.Context.ChannelId,
            operation.Context.ActorUserId, operation.Context.SubjectUserId, operation.Context.FocusDefinitionId!.Value,
            operation.Context.ChannelConversationId!.Value, operation.Context.Culture, operation.Context.TimeZoneId));
        Assert.Equal(document.StorageKey, operation.Attachment!.StorageKey);
        // The document waits for it with the context sealed, as a reviewer's change of form, and the step is of whoever asked.
        Assert.Equal((read.Value.RequestId, form.DefinitionId, asker, "en", "UTC"), (document.ExtractionRequestId!.Value, document.ExtractionDefinitionId!.Value,
            document.ExtractionActorUserId!.Value, document.ExtractionCulture, document.ExtractionTimeZoneId));
        var step = Assert.Single(f.Inbox.Events, item => item.DocumentId == document.Id && item.Kind == FormReceivedDocumentEventKind.ExtractionRequested);
        Assert.Equal(asker, step.ActorUserId);
        Assert.Equal("Tickets (en)", f.Contents().ReadDetail(document.Id, step.ProtectedDetail));
    }

    [Theory]
    [InlineData("discarded")]
    [InlineData("reading")]
    [InlineData("missing-form")]
    public async Task A_document_that_changed_or_a_form_that_is_gone_is_left_as_it_is(string kind)
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe")]);
        var document = f.Document();
        var now = f.Inbox.Clock.GetUtcNow().UtcDateTime;
        if (kind == "discarded") Assert.True(document.Discard(document.Revision, now).IsSuccess);
        if (kind == "reading")
            Assert.True(document.RequestExtraction(document.Revision, Guid.NewGuid(), form.DefinitionId, f.Reviewer, "es", "UTC", now).IsSuccess);
        var revision = document.Revision;

        var read = await f.Extractions().RereadAsync(new(document.Id, kind == "missing-form" ? Guid.NewGuid() : form.DefinitionId, f.Reviewer, "es", "UTC"), Ct);

        Assert.Equal(kind == "missing-form" ? FormReceivedDocumentErrors.FormUnavailable : FormReceivedDocumentErrors.StateConflict, read.Error);
        Assert.Empty(f.Engine.Submitted);
        Assert.Equal(revision, document.Revision);
    }

    [Fact]
    public async Task An_assistant_that_is_not_available_now_holds_the_document_a_minute_instead_of_leaving_it()
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe", required: true)]);
        var document = f.Document();
        var request = f.Request([document.Id], Guid.NewGuid());
        Assert.True(request.Draft(form.DefinitionId, Guid.NewGuid()).IsSuccess);
        var work = (await f.Rereads().ReserveAsync(request.Id, Ct)).Value!;
        var now = f.Inbox.Clock.GetUtcNow().UtcDateTime;
        f.Engine.SubmitFailure = Error.Conflict("Ai.Assistant.Unavailable", "The assistant is not available.");

        var read = await f.Extractions().RereadAsync(new(document.Id, form.DefinitionId, f.Reviewer, "es", "UTC"), Ct);
        var goOn = await f.Rereads().RecordAsync(new(request.Id, work.VersionId, document.Id, read.Error), Ct);

        // Paused, not configured or not qualified for now: the server tries it again in a minute, the document stays.
        Assert.Equal(FormReceivedDocumentErrors.AssistantUnavailable, read.Error);
        Assert.False(goOn.Value);
        Assert.Equal([document.Id], request.PendingRereadIds);
        Assert.Equal(now.Add(FormDocumentRereadService.RetryDelay), request.RereadAfterUtc);
    }

    [Fact]
    public async Task Changing_the_form_answers_the_engine_s_quota_as_it_is_with_its_code_and_wait()
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe", required: true)]);
        var document = f.Document();
        var quota = Error.TooManyRequests("Ai.Assistant.QuotaExceeded", "The acceptance quota is exhausted.",
            new Dictionary<string, object?> { ["retryAfter"] = 600 });
        f.Engine.SubmitFailure = quota;

        var changed = await f.Extractions().RequestAsync(document.Id, new(Guid.NewGuid(), form.DefinitionId, document.Revision), Ct);

        Assert.Equal(quota, changed.Error);
        Assert.Null(document.ExtractionRequestId);
    }

    [Fact]
    public async Task The_server_applies_a_reading_that_ended_and_leaves_one_that_still_runs()
    {
        var f = new FormDocumentProposalFixture();
        var form = f.Inbox.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe")]);
        var document = f.Document();
        var read = await f.Extractions().RereadAsync(new(document.Id, form.DefinitionId, f.Reviewer, "es", "UTC"), Ct);

        var running = await f.Extractions().ApplyAsync(document.Id, Ct);
        f.Engine.Complete(new(read.Value.RequestId, "failed", null, null, "Ai.Assistant.ProviderUnavailable"));
        var failed = await f.Extractions().ApplyAsync(document.Id, Ct);
        var nothing = await f.Extractions().ApplyAsync(document.Id, Ct);

        Assert.Equal("pending", running.Value.State);
        Assert.Equal(("failed", "Ai.Assistant.ProviderUnavailable"), (failed.Value.State, failed.Value.FailureCode));
        // As it was: unclassified, without the reading it waited for, and the failure is the step of whoever asked.
        Assert.Equal((FormReceivedDocumentState.Unclassified, (Guid?)null), (document.State, document.ExtractionRequestId));
        Assert.Equal(f.Reviewer, Assert.Single(f.Inbox.Events, item => item.Kind == FormReceivedDocumentEventKind.ExtractionFailed).ActorUserId);
        Assert.Equal(FormReceivedDocumentErrors.ExtractionNotFound, nothing.Error);
    }
}
