using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// F2 (ARCA H11): when an action stops its rule, whoever loaded the response is told once, in their language, by a channel that reaches them.
public sealed class FormActionStallNoticeTests
{
    [Fact]
    public async Task A_rule_that_stops_tells_its_author_once_in_their_language_even_if_it_is_retried_and_stops_again()
    {
        var (fixture, service, run) = await Stopped(reachable: true);
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        var message = Assert.Single(fixture.Channel.Messages);
        Assert.Equal(fixture.Runs.Records.Single().ActorUserId, message.RecipientUserId);
        Assert.Contains("is on hold", message.Body);
        Assert.Contains("Visits", message.Body);
        Assert.Equal(FormActionStallService.NoticeId(run.Id), message.RunId);
        Assert.NotNull(run.StallNoticedAtUtc);
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Single(fixture.Channel.Messages);
        Assert.True(run.RequestRetry(fixture.Clock.GetUtcNow().UtcDateTime).IsSuccess);
        var again = (await service.ReserveAsync(run.Id, CancellationToken.None)).Value!;
        Assert.True((await service.CompleteAsync(again, new(FormActionRunState.Failed, FormActionErrors.EffectUnconfirmed.Code, true), CancellationToken.None)).IsSuccess);
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Single(fixture.Channel.Messages);
    }

    [Fact]
    public async Task A_rule_that_goes_on_by_itself_tells_nobody()
    {
        var fixture = new FormActionFixture();
        var (record, snapshot) = fixture.Capture([new FormRule(Guid.NewGuid(), "Received", [], [FormActionFixture.Action("GeneratePdf"), FormActionFixture.Action()])]);
        fixture.Channel.Reachable.Add(record.ActorUserId);
        await fixture.Recorder.RecordAsync(record, snapshot, "Received", CancellationToken.None);
        var service = Dispatch(fixture, snapshot);
        var pdf = fixture.Runs.Runs.Single(run => run.Kind == "GeneratePdf");
        var work = (await service.ReserveAsync(pdf.Id, CancellationToken.None)).Value!;
        // In doubt by a local error: it asks again on its own, so the rule has not stopped.
        Assert.True((await service.CompleteAsync(work, new(FormActionRunState.Uncertain, FormActionErrors.EffectUnconfirmed.Code), CancellationToken.None)).IsSuccess);
        Assert.Equal(FormActionRunState.Running, pdf.State);
        Assert.Null((await service.ReserveAsync(pdf.Id, CancellationToken.None)).Value);
        fixture.Clock.Advance(BusinessFormActionRun.AutoConsultDelay(0));
        var consult = (await service.ReserveAsync(pdf.Id, CancellationToken.None)).Value!;
        Assert.True((await service.CompleteAsync(consult, new(FormActionRunState.Completed), CancellationToken.None)).IsSuccess);
        Assert.Null((await service.ReserveAsync(pdf.Id, CancellationToken.None)).Value);
        Assert.Empty(fixture.Channel.Messages);
        Assert.Null(pdf.StallNoticedAtUtc);
        Assert.All(fixture.Runs.Runs, run => Assert.False(run.NeedsStallNotice));
    }

    [Fact]
    public async Task An_action_in_doubt_that_ran_out_of_consults_tells_its_author_once()
    {
        var fixture = new FormActionFixture();
        var (record, snapshot) = fixture.Capture([new FormRule(Guid.NewGuid(), "Received", [], [FormActionFixture.Action("GeneratePdf")])]);
        fixture.Channel.Reachable.Add(record.ActorUserId);
        await fixture.Recorder.RecordAsync(record, snapshot, "Received", CancellationToken.None);
        var service = Dispatch(fixture, snapshot);
        var run = Assert.Single(fixture.Runs.Runs);
        for (var attempt = 0; attempt <= BusinessFormActionRun.MaxAutoConsults; attempt++)
        {
            var work = (await service.ReserveAsync(run.Id, CancellationToken.None)).Value!;
            Assert.True((await service.CompleteAsync(work, new(FormActionRunState.Uncertain, FormActionErrors.EffectUnconfirmed.Code), CancellationToken.None)).IsSuccess);
            if (attempt < BusinessFormActionRun.MaxAutoConsults) Assert.Empty(fixture.Channel.Messages);
            fixture.Clock.Advance(TimeSpan.FromMinutes(20));
        }
        Assert.Equal(FormActionRunState.Uncertain, run.State);
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Single(fixture.Channel.Messages);
    }

    [Fact]
    public async Task An_author_without_a_channel_to_hear_it_is_not_told_and_nothing_waits_for_them()
    {
        var (fixture, service, run) = await Stopped(reachable: false);
        Assert.True(run.NeedsStallNotice);
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Empty(fixture.Channel.Messages);
        Assert.NotNull(run.StallNoticedAtUtc);
        Assert.False(run.NeedsStallNotice);
    }

    [Fact]
    public async Task A_notice_that_cannot_be_enqueued_leaves_the_run_unmarked_so_the_worker_tries_again()
    {
        var (fixture, service, run) = await Stopped(reachable: true);
        fixture.Channel.ReadException = new InvalidOperationException("down");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReserveAsync(run.Id, CancellationToken.None));
        Assert.True(run.NeedsStallNotice);
        fixture.Channel.ReadException = null;
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Single(fixture.Channel.Messages);
    }

    [Fact]
    public async Task A_notice_the_closed_window_refuses_is_marked_resolved_without_telling_anyone_and_is_not_asked_again()
    {
        var (fixture, service, run) = await Stopped(reachable: true, answer: new(StructuredRecordActionState.Failed, "WhatsApp.FormAction.WindowClosed", true));
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Empty(fixture.Channel.Messages);
        Assert.NotNull(run.StallNoticedAtUtc);
        Assert.False(run.NeedsStallNotice);
        var reads = fixture.Channel.Reads.Count;
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Equal(reads, fixture.Channel.Reads.Count);
    }

    [Fact]
    public async Task A_notice_the_channel_refuses_for_any_other_reason_leaves_the_run_unmarked_and_comes_back_when_it_can()
    {
        var (fixture, service, run) = await Stopped(reachable: true, answer: new(StructuredRecordActionState.Failed, "WhatsApp.FormAction.NotLinked", true));
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Empty(fixture.Channel.Messages);
        Assert.Null(run.StallNoticedAtUtc);
        Assert.True(run.NeedsStallNotice);
        fixture.Channel.EnqueueAnswer = null;
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Single(fixture.Channel.Messages);
        Assert.NotNull(run.StallNoticedAtUtc);
    }

    [Fact]
    public async Task An_issuance_waiting_for_its_author_to_confirm_it_is_a_question_not_a_stop()
    {
        var fixture = new FormActionFixture();
        var (record, snapshot) = fixture.Capture([new FormRule(Guid.NewGuid(), "Received", [], [FormActionFixture.Action("IssueDocument")])]);
        await fixture.Recorder.RecordAsync(record, snapshot, "Received", CancellationToken.None);
        var run = Assert.Single(fixture.Runs.Runs);
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        var fence = run.Reserve(now, TimeSpan.FromMinutes(2)).Value.Fence;
        Assert.True(run.RecordOutcome(fence, now, FormActionRunState.Uncertain, FormActionErrors.AwaitingConfirmation.Code, false).IsSuccess);
        Assert.False(run.NeedsStallNotice);
    }

    private static async Task<(FormActionFixture Fixture, WorkerPass Service, BusinessFormActionRun Run)> Stopped(bool reachable, StructuredRecordActionResponse? answer = null)
    {
        var fixture = new FormActionFixture();
        fixture.Channel.EnqueueAnswer = answer;
        var (record, snapshot) = fixture.Capture([new FormRule(Guid.NewGuid(), "Received", [], [FormActionFixture.Action("GeneratePdf"), FormActionFixture.Action()])]);
        if (reachable) fixture.Channel.Reachable.Add(record.ActorUserId);
        await fixture.Recorder.RecordAsync(record, snapshot, "Received", CancellationToken.None);
        var service = Dispatch(fixture, snapshot);
        var run = fixture.Runs.Runs.Single(item => item.Kind == "GeneratePdf");
        var work = (await service.ReserveAsync(run.Id, CancellationToken.None)).Value!;
        Assert.True((await service.CompleteAsync(work, new(FormActionRunState.Failed, FormActionErrors.EffectUnconfirmed.Code, true), CancellationToken.None)).IsSuccess);
        Assert.Equal(FormActionRunState.Failed, run.State);
        return (fixture, service, run);
    }

    private static WorkerPass Dispatch(FormActionFixture fixture, FormActionRecordRow snapshot) =>
        new(new(fixture.Runs, fixture.Runs, fixture.Rules, new FormActionRoleGuard(fixture.Rules, fixture.Recipients), fixture.Rules.UnitOfWork,
            new FormActionOutcomeRecorder(new FormPdfDocumentRecorder(new MemoryFormDocumentRepository(), new MemoryStoredFileRepository()),
                new FormRecordResultRecorder(new IgnoredResults(), fixture.Clock), fixture.Runs,
                new FormIssuedCatalogItemRecorder(null!, null!, null!, null!, null!), new FakeEmailQueue(), new FormRecordVoidRecorder(fixture.Runs)),
            fixture.Clock, NullLogger<FormActionDispatchService>.Instance),
            new FormActionStallService(fixture.Runs, new FakeFormResponseReader { ExportRows = [snapshot.Response] }, [fixture.Channel], fixture.Recipients,
                fixture.Rules.UnitOfWork, fixture.Clock, NullLogger<FormActionStallService>.Instance));

    // What the worker does with a run: reserve it and, when it cannot go on, tell whoever loaded the response.
    private sealed class WorkerPass(FormActionDispatchService dispatch, FormActionStallService stalls)
    {
        public async Task<ArquitecturaBase.Domain.Results.Result<FormActionWorkResponse?>> ReserveAsync(Guid runId, CancellationToken ct)
        {
            var result = await dispatch.ReserveAsync(runId, ct);
            if (result.IsSuccess && result.Value is null) await stalls.NotifyAsync(runId, ct);
            return result;
        }

        public Task<ArquitecturaBase.Domain.Results.Result> CompleteAsync(FormActionWorkResponse work, FormActionOutcomeResponse outcome, CancellationToken ct) =>
            dispatch.CompleteAsync(work, outcome, ct);
    }

    private sealed class IgnoredResults : IFormRecordResultRepository
    {
        public void Add(BusinessFormRecordResult result) { }
    }
}
