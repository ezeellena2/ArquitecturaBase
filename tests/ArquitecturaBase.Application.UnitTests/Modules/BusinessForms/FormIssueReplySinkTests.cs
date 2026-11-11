using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
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

    // A response loaded from a channel (number 1 of «VISIT») whose «IssueDocument» run waits for its author.
    private sealed class Host
    {
        public FormActionFixture Fixture { get; } = new();
        public BusinessFormRecord Record { get; }
        public BusinessFormActionRun Run { get; }
        public BusinessFormIssueReplySink Sink { get; }

        public Host(string culture)
        {
            (Record, _) = Fixture.Capture([], presentationCulture: culture);
            Fixture.Recipients.People[Record.ActorUserId] = new(Record.ActorUserId, culture, "Author");
            var now = Fixture.Clock.GetUtcNow().UtcDateTime;
            Run = BusinessFormActionRun.Create(Record.DefinitionId, Record.Id, Record.RuleSetId!.Value, Guid.NewGuid(), Guid.NewGuid(), "Received",
                "IssueDocument", 0, null, "{}", true, now);
            Run.RecordOutcome(Run.Reserve(now, TimeSpan.FromMinutes(2)).Value.Fence, now, FormActionRunState.Uncertain,
                FormActionErrors.AwaitingConfirmation.Code, false);
            Fixture.Runs.Runs.Add(Run);
            var rules = Fixture.Rules;
            var service = new FormIssueConfirmationService(new(rules.User, rules.Permissions, new InMemoryUserAccounts()), Fixture.Runs, rules,
                Fixture.Recipients, new(Fixture.Runs, new(rules.User, rules.Permissions, new FakeFormResponseReader()), rules.Permissions, [], []),
                rules.UnitOfWork, Fixture.Clock, NullLogger<FormIssueConfirmationService>.Instance);
            Sink = new(service);
        }
    }
}
