using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// DOC6 (task 14): asking for a form recovers what the engine already took under the request's UUID (ADR 0018), with an engine that keeps a
// receipt per request as the real one does. Two attempts at once, or a retry after its save failed, answer the same request and the engine
// runs one operation; another document under that UUID is a conflict. Reading it says why it failed.
public sealed class FormDocumentAuthoringServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_identical_requests_at_once_answer_one_request_and_the_engine_runs_one_operation()
    {
        var f = new FormDocumentProposalFixture();
        var document = f.Document();
        var requestId = Guid.NewGuid();
        Result<FormDocumentProposalResponse>? first = null;
        // The other attempt gets to the engine a second later, between this one's look and its submission, and keeps its request.
        f.Engine.BeforeSubmit = async _ =>
        {
            f.Inbox.Clock.Advance(TimeSpan.FromSeconds(1));
            first = await f.Authoring().RequestAsync(new(requestId, document.Id, null), Ct);
        };

        var second = await f.Authoring().RequestAsync(new(requestId, document.Id, null), Ct);

        Assert.True(first!.IsSuccess, first.IsFailure ? first.Error.Code : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.Code : null);
        Assert.Equal(("pending", "pending"), (first.Value.State, second.Value.State));
        Assert.Single(f.Engine.Submitted);
        Assert.Equal(1, f.Engine.Replays);
        Assert.Equal(requestId, Assert.Single(f.Proposals).RequestId);
    }

    [Fact]
    public async Task Another_document_under_a_request_the_engine_already_took_is_a_conflict_and_keeps_nothing_else()
    {
        var f = new FormDocumentProposalFixture();
        var taken = f.Document();
        var other = f.Document();
        var requestId = Guid.NewGuid();
        f.Engine.BeforeSubmit = async _ =>
        {
            f.Inbox.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True((await f.Authoring().RequestAsync(new(requestId, taken.Id, null), Ct)).IsSuccess);
        };

        var reused = await f.Authoring().RequestAsync(new(requestId, other.Id, null), Ct);

        Assert.Equal(FormReceivedDocumentErrors.RequestConflict, reused.Error);
        Assert.Equal([taken.Id], Assert.Single(f.Proposals).DocumentIds);
        Assert.Single(f.Engine.Submitted);
    }

    [Fact]
    public async Task A_retry_after_its_save_failed_recovers_the_request_the_engine_kept_without_a_second_operation()
    {
        var f = new FormDocumentProposalFixture();
        var document = f.Document();
        var requestId = Guid.NewGuid();
        f.LockFailure = new InvalidOperationException("The database went away.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Authoring().RequestAsync(new(requestId, document.Id, null), Ct));
        Assert.Empty(f.Proposals);
        Assert.Equal(FormReceivedDocumentErrors.ProposalNotFound, (await f.Authoring().GetAsync(requestId, Ct)).Error);
        f.Inbox.Clock.Advance(TimeSpan.FromMinutes(1));
        var retried = await f.Authoring().RequestAsync(new(requestId, document.Id, null), Ct);
        var read = await f.Authoring().GetAsync(requestId, Ct);

        Assert.True(retried.IsSuccess, retried.IsFailure ? retried.Error.Code : null);
        Assert.Equal(("pending", "pending"), (retried.Value.State, read.Value.State));
        var operation = Assert.Single(f.Engine.Submitted);
        Assert.Equal(1, f.Engine.Replays);
        // The request is kept as the engine took it: its document and the instant it was sealed at.
        var kept = Assert.Single(f.Proposals);
        Assert.Equal([document.Id], kept.DocumentIds);
        Assert.Equal(operation.OccurredAtUtc, kept.RequestedAtUtc);
    }

    [Fact]
    public async Task A_retry_for_another_document_after_its_save_failed_is_a_conflict()
    {
        var f = new FormDocumentProposalFixture();
        var taken = f.Document();
        var requestId = Guid.NewGuid();
        f.LockFailure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Authoring().RequestAsync(new(requestId, taken.Id, null), Ct));
        var reused = await f.Authoring().RequestAsync(new(requestId, f.Document().Id, null), Ct);

        Assert.Equal(FormReceivedDocumentErrors.RequestConflict, reused.Error);
        Assert.Empty(f.Proposals);
        Assert.Single(f.Engine.Submitted);
    }

    [Fact]
    public async Task An_engine_conflict_that_is_not_the_request_taken_before_is_the_engine_s_failure_and_keeps_nothing()
    {
        var f = new FormDocumentProposalFixture();
        var document = f.Document();
        // The assistant paused, not configured or with its model not qualified: a conflict of the engine's own, not the UUID taken before.
        f.Engine.SubmitFailure = Error.Conflict("Ai.Assistant.Unavailable", "The assistant is not available.");

        var refused = await f.Authoring().RequestAsync(new(Guid.NewGuid(), document.Id, null), Ct);

        Assert.Equal(FormReceivedDocumentErrors.ProposalUnavailable, refused.Error);
        Assert.Empty(f.Proposals);
    }

    [Fact]
    public async Task A_follow_up_lookup_the_engine_refuses_after_its_conflict_is_a_refusal_and_keeps_nothing()
    {
        var f = new FormDocumentProposalFixture();
        var document = f.Document();
        // The submission collides (as if the UUID were taken before) and the lookup that would find it is refused: the engine's refusal (403).
        f.Engine.SubmitFailure = ScriptedFormProposalEngine.RequestReused;
        f.Engine.BeforeSubmit = _ =>
        {
            f.Engine.ReadFailure = Error.Forbidden("Ai.Assistant.Forbidden", "The assistant refused the request.");
            return Task.CompletedTask;
        };

        var refused = await f.Authoring().RequestAsync(new(Guid.NewGuid(), document.Id, null), Ct);

        Assert.Equal(FormDefinitionErrors.Forbidden, refused.Error);
        Assert.Equal(2, f.Engine.Reads);
        Assert.Empty(f.Proposals);
    }

    [Fact]
    public async Task A_request_the_engine_took_with_another_context_that_its_lookup_still_finds_is_a_conflict()
    {
        var f = new FormDocumentProposalFixture();
        var document = f.Document();
        var requestId = Guid.NewGuid();
        // An earlier attempt with fewer capabilities (an earlier version of the channel): the lookup finds it, and its content is not this one.
        var rows = (await f.Policy().DocumentAsync(document.Id, Ct)).Value;
        var context = FormDocumentAuthoringPolicy.Context(f.Reviewer, "es", InMemoryUserAccounts.DefaultTimeZoneId, requestId);
        var earlier = f.Policy().Request(requestId, context with { AllowedCapabilities = [.. context.AllowedCapabilities.Take(3)] }, rows,
            f.Inbox.Clock.GetUtcNow().UtcDateTime.AddMinutes(-5))!;
        Assert.True(f.Engine.Take(earlier).IsSuccess);

        var reused = await f.Authoring().RequestAsync(new(requestId, document.Id, null), Ct);

        Assert.Equal(FormReceivedDocumentErrors.RequestConflict, reused.Error);
        Assert.Empty(f.Proposals);
        Assert.Single(f.Engine.Submitted);
    }

    [Fact]
    public async Task The_engine_s_quota_comes_back_as_it_is_with_its_code_and_wait_and_keeps_nothing()
    {
        var f = new FormDocumentProposalFixture();
        var quota = Error.TooManyRequests("Ai.Assistant.QuotaExceeded", "The acceptance quota is exhausted.",
            new Dictionary<string, object?> { ["retryAfter"] = 600 });
        f.Engine.SubmitFailure = quota;

        var refused = await f.Authoring().RequestAsync(new(Guid.NewGuid(), f.Document().Id, null), Ct);

        Assert.Equal(quota, refused.Error);
        Assert.Empty(f.Proposals);
    }

    [Fact]
    public async Task Reading_says_why_it_failed_when_the_assistant_proposed_no_form_or_the_engine_no_longer_knows_it()
    {
        var f = new FormDocumentProposalFixture();
        var finished = Guid.NewGuid();
        var forgotten = Guid.NewGuid();
        Assert.True((await f.Authoring().RequestAsync(new(finished, f.Document().Id, null), Ct)).IsSuccess);
        Assert.True((await f.Authoring().RequestAsync(new(forgotten, f.Document().Id, null), Ct)).IsSuccess);
        f.Engine.Complete(new(finished, "completed", null, null, null));
        f.Engine.Forget(forgotten);

        var noProposal = await f.Authoring().GetAsync(finished, Ct);
        var unknown = await f.Authoring().GetAsync(forgotten, Ct);

        Assert.Equal(("failed", FormDocumentAuthoringPolicy.NoProposal, (FormDocumentProposalContentResponse?)null),
            (noProposal.Value.State, noProposal.Value.FailureCode, noProposal.Value.Proposal));
        Assert.Equal(("failed", FormReceivedDocumentErrors.ProposalNotFound.Code), (unknown.Value.State, unknown.Value.FailureCode));
        Assert.Single(unknown.Value.Documents);
    }
}
