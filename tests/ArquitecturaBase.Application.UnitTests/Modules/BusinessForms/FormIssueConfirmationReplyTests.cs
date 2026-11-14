using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

/// <summary>
/// «¿La emito?» (EM2-A) from a button of a channel: «No emitir» voids the response and hands back what the channel answers, with
/// the response named as the app names it and in the language of the person; «Emitir» and a stale or foreign button hand back no
/// text. The decision takes the current permissions of the author and a response that still stands.
/// </summary>
public sealed class FormIssueConfirmationReplyTests
{
    private static readonly StructuredRecordActionReply Ignored = new(StructuredRecordActionReplyState.Ignored);

    [Theory]
    [InlineData("es", "No la emitimos. VISIT-000001 quedó anulada.")]
    [InlineData("en", "We did not issue it. VISIT-000001 was voided.")]
    public async Task Not_issuing_voids_the_response_and_hands_back_the_answer_in_the_language_of_the_person(string culture, string expected)
    {
        var host = new Host(culture);

        var reply = await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied, expected), reply);
        Assert.Equal(FormRecordState.Voided, host.Record.State);
        Assert.Equal(FormActionRunState.Running, host.Run.State);
        Assert.Equal(1, host.Fixture.Rules.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Issuing_releases_the_run_and_hands_back_no_text()
    {
        var host = new Host("es");

        var reply = await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Issue, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied), reply);
        Assert.Equal((FormActionRunState.Running, true), (host.Run.State, host.Run.DeliveryReviewWasSent));
        Assert.Equal(FormRecordState.Received, host.Record.State);
    }

    // H21: another tap after the decision (a second tap of the person) changes nothing and is not answered.
    [Fact]
    public async Task Another_tap_after_a_decision_is_ignored_without_a_text()
    {
        var host = new Host("es");
        await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Issue, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(1, host.Fixture.Rules.UnitOfWork.Commits);
    }

    // H21: the same tap handed again (the channel did not get to answer it) gets the same answer, without deciding again.
    [Fact]
    public async Task The_same_tap_handed_again_gets_the_same_answer_without_deciding_again()
    {
        var host = new Host("es");
        var tap = Guid.NewGuid();

        var first = await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, tap, CancellationToken.None);
        var again = await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, tap, CancellationToken.None);

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied, "No la emitimos. VISIT-000001 quedó anulada."), first);
        Assert.Equal(first, again);
        Assert.Equal((FormRecordState.Voided, tap), (host.Record.State, host.Run.DecisionTapId));
    }

    [Fact]
    public async Task The_same_tap_on_issuing_is_applied_again_without_asking_twice()
    {
        var host = new Host("es");
        var tap = Guid.NewGuid();
        await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Issue, tap, CancellationToken.None);
        var reviewed = host.Run.DeliveryReviewedAtUtc;
        host.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied),
            await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Issue, tap, CancellationToken.None));
        Assert.Equal((reviewed, tap), (host.Run.DeliveryReviewedAtUtc, host.Run.DecisionTapId));
    }

    // H21: the same «Volver a probar» tap handed again when the run already asks anew is answered the same, without asking again.
    [Fact]
    public async Task The_same_retry_tap_after_the_run_asks_again_does_not_ask_twice()
    {
        var preview = new StructuredIssuePreview(null, null, null, "Arca.Registry.PersonNotFound");
        var host = new Host("es", preview);
        var tap = Guid.NewGuid();
        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied),
            await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Retry, tap, CancellationToken.None));
        // The run asked for the summary again, it failed again and waits for a new decision.
        var now = host.Fixture.Clock.GetUtcNow().UtcDateTime;
        Assert.True(host.Run.RecordOutcome(host.Run.Reserve(now, TimeSpan.FromMinutes(2)).Value.Fence, now, FormActionRunState.Uncertain,
            FormActionErrors.AwaitingConfirmation.Code, false,
            FormDraftPolicy.Serialize(new FormActionOutcomeResponse(FormActionRunState.Uncertain, FormActionErrors.AwaitingConfirmation.Code, IssuePreview: preview))).IsSuccess);

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied),
            await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Retry, tap, CancellationToken.None));
        Assert.Equal((FormActionRunState.Uncertain, FormActionErrors.AwaitingConfirmation.Code), (host.Run.State, host.Run.FailureCode));
    }

    [Fact]
    public async Task A_button_of_another_person_another_choice_or_another_run_is_ignored_and_changes_nothing()
    {
        var host = new Host("es");

        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, Guid.NewGuid(), FormIssueConfirmationPolicy.Skip, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, "other", Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(Ignored, await host.Sink.ReplyAsync(Guid.NewGuid(), host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(FormRecordState.Received, host.Record.State);
        Assert.Equal((FormActionRunState.Uncertain, FormActionErrors.AwaitingConfirmation.Code), (host.Run.State, host.Run.FailureCode));
        Assert.Equal(0, host.Fixture.Rules.UnitOfWork.Commits);
    }

    // Decision 4: after a summary that failed, «Volver a probar» asks for it again, with no text in the act (the run sends what comes
    // next); «Emitir» does not apply to a summary that failed, nor «Volver a probar» to one that came out.
    [Fact]
    public async Task Trying_a_failed_summary_again_releases_the_run_and_issuing_does_not_apply_to_it()
    {
        var host = new Host("es", new StructuredIssuePreview(null, null, null, "Arca.Registry.PersonNotFound"));

        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Issue, Guid.NewGuid(), CancellationToken.None));
        var reply = await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Retry, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied), reply);
        Assert.Equal((FormActionRunState.Running, true), (host.Run.State, host.Run.DeliveryReviewWasSent));
        Assert.Equal(FormRecordState.Received, host.Record.State);
        Assert.Equal(Ignored, await new Host("es").Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Retry, Guid.NewGuid(), CancellationToken.None));
    }

    // A message after «Volver a probar» has an id of its own: its buttons name the run, and «No emitir» there voids as anywhere.
    [Fact]
    public async Task A_button_of_a_message_after_trying_again_reaches_the_run_it_names()
    {
        var host = new Host("en", new StructuredIssuePreview(null, null, null, "Arca.Connection.Unavailable"));
        var skip = FormIssueConfirmationPolicy.Choices(true, "en", host.Run.Id)[1].Id;

        var reply = await host.Sink.ReplyAsync(FormIssueConfirmationPolicy.MessageId(host.Run.Id, 1), host.Record.ActorUserId, skip, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied, "We did not issue it. VISIT-000001 was voided."), reply);
        Assert.Equal((FormRecordState.Voided, FormActionErrors.NotIssued.Code, (string?)null), (host.Record.State, host.Record.VoidReasonCode, host.Record.VoidReason));
    }

    // The author decides with the permissions they have now: without «Completar formularios» (nor «Corregir y anular respuestas»
    // with «Ver respuestas») a button does not apply, and an inactive account neither.
    [Fact]
    public async Task A_button_of_an_author_who_lost_the_permission_does_not_apply_and_changes_nothing()
    {
        var host = new Host("es");
        host.Fixture.Rules.Permissions.Permissions[host.Record.ActorUserId] = [];

        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Issue, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(FormRecordState.Received, host.Record.State);
        Assert.Equal((FormActionRunState.Uncertain, false), (host.Run.State, host.Run.DeliveryReviewRequested));
    }

    // A response that no longer stands (voided in the app while «¿La emito?» waited) is neither issued nor voided again.
    [Fact]
    public async Task A_decision_on_a_response_that_no_longer_stands_does_not_apply()
    {
        var host = new Host("es");
        Assert.True(host.Record.Void(Guid.NewGuid(), "Se cargó mal", host.Fixture.Clock.GetUtcNow().UtcDateTime).IsSuccess);

        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Issue, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(("Se cargó mal", false), (host.Record.VoidReason, host.Run.DeliveryReviewRequested));
    }

    // A response loaded from a channel (number 1 of «VISIT») whose «IssueDocument» run waits for its author, after a summary that
    // came out (or, with a failure code, one that failed).
    private sealed class Host
    {
        public FormActionFixture Fixture { get; } = new();
        public BusinessFormRecord Record { get; }
        public BusinessFormActionRun Run { get; }
        public FormIssueConfirmationService Sink { get; }

        public Host(string culture, StructuredIssuePreview? preview = null)
        {
            var accounts = new InMemoryUserAccounts();
            var author = accounts.AddUser("autor@example.com");
            Fixture.Rules.User.UserId = author.Id;
            Fixture.Rules.Permissions.Permissions[author.Id] = [BusinessFormsPermissions.RecordsCreate];
            (Record, _) = Fixture.Capture([], presentationCulture: culture);
            Fixture.Recipients.People[Record.ActorUserId] = new(Record.ActorUserId, culture, "Author");
            var now = Fixture.Clock.GetUtcNow().UtcDateTime;
            Run = BusinessFormActionRun.Create(Record.DefinitionId, Record.Id, Record.RuleSetId!.Value, Guid.NewGuid(), Guid.NewGuid(), "Received",
                "IssueDocument", 0, null, "{}", true, now);
            Run.RecordOutcome(Run.Reserve(now, TimeSpan.FromMinutes(2)).Value.Fence, now, FormActionRunState.Uncertain,
                FormActionErrors.AwaitingConfirmation.Code, false, preview is null ? null
                    : FormDraftPolicy.Serialize(new FormActionOutcomeResponse(FormActionRunState.Uncertain, FormActionErrors.AwaitingConfirmation.Code, IssuePreview: preview)));
            Fixture.Runs.Runs.Add(Run);
            var rules = Fixture.Rules;
            var decisions = new FormIssueDecisionRecorder(new(rules.User, rules.Permissions, accounts), Fixture.Runs, rules, new(Fixture.Runs), Fixture.Clock);
            Sink = new(new(rules.User, rules.Permissions, accounts), decisions,
                new(Fixture.Runs, new(rules.User, rules.Permissions, new FakeFormResponseReader()), rules.Permissions, [], []),
                Fixture.Recipients, rules.UnitOfWork, NullLogger<FormIssueConfirmationService>.Instance);
        }
    }
}
