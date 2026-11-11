using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

/// <summary>
/// «¿La emito?» (EM2-A) from a button of a channel: «No emitir» voids the response and hands back what the channel answers, with
/// the response named as the app names it and in the language of the person; «Emitir» and a stale or foreign button hand back no text.
/// </summary>
public sealed class FormIssueReplySinkTests
{
    private static readonly StructuredRecordActionReply Ignored = new(StructuredRecordActionReplyState.Ignored);

    [Theory]
    [InlineData("es", "No la emitimos. VISIT-000001 quedó anulada.")]
    [InlineData("en", "We did not issue it. VISIT-000001 was voided.")]
    public async Task Not_issuing_voids_the_response_and_hands_back_the_answer_in_the_language_of_the_person(string culture, string expected)
    {
        var host = new Host(culture);

        var reply = await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, CancellationToken.None);

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied, expected), reply);
        Assert.Equal(FormRecordState.Voided, host.Record.State);
        Assert.Equal(FormActionRunState.Running, host.Run.State);
        Assert.Equal(1, host.Fixture.Rules.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Issuing_releases_the_run_and_hands_back_no_text()
    {
        var host = new Host("es");

        var reply = await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Issue, CancellationToken.None);

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied), reply);
        Assert.Equal((FormActionRunState.Running, true), (host.Run.State, host.Run.DeliveryReviewWasSent));
        Assert.Equal(FormRecordState.Received, host.Record.State);
    }

    [Fact]
    public async Task A_repeated_decision_is_ignored_without_a_text()
    {
        var host = new Host("es");
        await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, CancellationToken.None);

        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, CancellationToken.None));
        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Issue, CancellationToken.None));
        Assert.Equal(1, host.Fixture.Rules.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_button_of_another_person_another_choice_or_another_run_is_ignored_and_changes_nothing()
    {
        var host = new Host("es");

        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, Guid.NewGuid(), FormIssueConfirmationPolicy.Skip, CancellationToken.None));
        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, "other", CancellationToken.None));
        Assert.Equal(Ignored, await host.Sink.ReplyAsync(Guid.NewGuid(), host.Record.ActorUserId, FormIssueConfirmationPolicy.Skip, CancellationToken.None));

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

        Assert.Equal(Ignored, await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Issue, CancellationToken.None));
        var reply = await host.Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Retry, CancellationToken.None);

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied), reply);
        Assert.Equal((FormActionRunState.Running, true), (host.Run.State, host.Run.DeliveryReviewWasSent));
        Assert.Equal(FormRecordState.Received, host.Record.State);
        Assert.Equal(Ignored, await new Host("es").Sink.ReplyAsync(host.Run.Id, host.Record.ActorUserId, FormIssueConfirmationPolicy.Retry, CancellationToken.None));
    }

    // A message after «Volver a probar» has an id of its own: its buttons name the run, and «No emitir» there voids as anywhere.
    [Fact]
    public async Task A_button_of_a_message_after_trying_again_reaches_the_run_it_names()
    {
        var host = new Host("en", new StructuredIssuePreview(null, null, null, "Arca.Connection.Unavailable"));
        var skip = FormIssueConfirmationPolicy.Choices(true, "en", host.Run.Id)[1].Id;

        var reply = await host.Sink.ReplyAsync(FormIssueConfirmationPolicy.MessageId(host.Run.Id, 1), host.Record.ActorUserId, skip, CancellationToken.None);

        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied, "We did not issue it. VISIT-000001 was voided."), reply);
        Assert.Equal((FormRecordState.Voided, "It was not issued."), (host.Record.State, host.Record.VoidReason));
    }

    // A response loaded from a channel (number 1 of «VISIT») whose «IssueDocument» run waits for its author, after a summary that
    // came out (or, with a failure code, one that failed).
    private sealed class Host
    {
        public FormActionFixture Fixture { get; } = new();
        public BusinessFormRecord Record { get; }
        public BusinessFormActionRun Run { get; }
        public BusinessFormIssueReplySink Sink { get; }

        public Host(string culture, StructuredIssuePreview? preview = null)
        {
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
            var service = new FormIssueConfirmationService(new(rules.User, rules.Permissions, new InMemoryUserAccounts()), Fixture.Runs, rules,
                Fixture.Recipients, new(Fixture.Runs, new(rules.User, rules.Permissions, new FakeFormResponseReader()), rules.Permissions, [], []),
                rules.UnitOfWork, Fixture.Clock, NullLogger<FormIssueConfirmationService>.Instance);
            Sink = new(service);
        }
    }
}
