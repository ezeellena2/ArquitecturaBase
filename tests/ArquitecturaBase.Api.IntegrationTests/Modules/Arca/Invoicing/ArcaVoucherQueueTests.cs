using System.Globalization;
using System.Xml.Linq;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Registry;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>
/// La cola de emisión de punta a punta con PostgreSQL y FakeArca (ADR 0028): número y huella antes de pedir, reconciliación
/// ante una respuesta perdida, un 502 o un 500, revisión humana ante un número usado o una fecha vencida, y dos hosts sobre
/// la misma secuencia. Ninguna prueba emite dos veces ni saltea un número. El worker está apagado: cada test procesa a mano.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaVoucherQueueTests(ApiFactory factory) : IDisposable
{
    private static readonly XNamespace Fev1 = "http://ar.gov.afip.dif.FEV1/";
    private readonly FakeArcaCertificateAuthority authority = new();

    [Fact]
    public async Task An_order_is_queued_once_and_issued_with_the_next_number_and_its_cae()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 11);
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            var first = await EnqueueAsync(order);
            Assert.Equal(first.Id, (await EnqueueAsync(order)).Id);
            Assert.Equal((ArcaVoucherState.Queued, ArcaVoucherClass.C, 11, 605500m), (first.State, first.Class, first.VoucherType, first.Total));

            await ProcessAsync();

            var issued = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)12, arca.Vouchers.Issued.Single().Cae, (DateOnly?)Today, (DateOnly?)Today.AddDays(10)),
                (issued.State, issued.Number, issued.Cae, issued.IssuedOn, issued.CaeDueOn));
            // The receiver it fixed when queued comes back with the voucher, queued and read.
            Assert.Equal((order.Receiver, order.Receiver), (first.Receiver, issued.Receiver));
            Assert.Equal(1, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));

            var next = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(next);
            await ProcessAsync();
            Assert.Equal((long?)13, (await VoucherAsync(next.RequestKey)).Number);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_response_lost_after_arca_issued_is_reconciled_without_issuing_twice()
    {
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(order);
            arca.Vouchers.Enqueue(FakeArcaVoucherFault.LoseResponseAfterAuthorizing);

            await ProcessAsync();
            var uncertain = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Uncertain, (long?)1, true), (uncertain.State, uncertain.Number, uncertain.WaitedForArca));

            await ProcessAsync();
            Assert.Equal(0, arca.Calls("FECompConsultar", FakeArca.InvoicingService));

            factory.Clock.Advance(TimeSpan.FromSeconds(30));
            await ProcessAsync();

            var issued = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)1, arca.Vouchers.Issued.Single().Cae), (issued.State, issued.Number, issued.Cae));
            Assert.Equal((1, 1), (arca.Calls("FECAESolicitar", FakeArca.InvoicingService), arca.Calls("FECompConsultar", FakeArca.InvoicingService)));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Theory]
    [InlineData(FakeArcaVoucherFaultCase.LoseRequest)]
    [InlineData(FakeArcaVoucherFaultCase.TransactionActive)]
    [InlineData(FakeArcaVoucherFaultCase.InternalError)]
    public async Task What_arca_did_not_issue_is_sent_again_with_the_same_number(FakeArcaVoucherFaultCase fault)
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 6);
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(order);
            arca.Vouchers.Enqueue(fault switch
            {
                FakeArcaVoucherFaultCase.LoseRequest => FakeArcaVoucherFault.LoseRequest,
                FakeArcaVoucherFaultCase.TransactionActive => FakeArcaVoucherFault.TransactionActive,
                _ => FakeArcaVoucherFault.InternalError,
            });

            await ProcessAsync();
            Assert.Equal((ArcaVoucherState.Uncertain, (long?)7), ((await VoucherAsync(order.RequestKey)).State, (await VoucherAsync(order.RequestKey)).Number));

            factory.Clock.Advance(TimeSpan.FromSeconds(30));
            await ProcessAsync();

            var issued = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)7), (issued.State, issued.Number));
            Assert.Equal(7, Assert.Single(arca.Vouchers.Issued).Number);
            Assert.Equal(2, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_rejection_frees_the_number_for_the_next_voucher()
    {
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var rejected = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(rejected);
            arca.Vouchers.Enqueue(FakeArcaVoucherFault.Reject);
            await ProcessAsync();
            var row = await VoucherAsync(rejected.RequestKey);
            Assert.Equal((ArcaVoucherState.Rejected, (long?)null, "wsfe.10015"), (row.State, row.Number, row.FailureCode));

            var next = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(next);
            await ProcessAsync();
            Assert.Equal((ArcaVoucherState.Authorized, (long?)1), ((await VoucherAsync(next.RequestKey)).State, (await VoucherAsync(next.RequestKey)).Number));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task When_arca_does_not_answer_the_voucher_waits_without_a_number_and_goes_out_later()
    {
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(order);
            arca.Fail(FakeArca.InvoicingService, FakeArcaFailure.ServiceUnavailable);

            await ProcessAsync();
            var waiting = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Queued, (long?)null, true), (waiting.State, waiting.Number, waiting.WaitedForArca));
            Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime.AddSeconds(30), waiting.NextAttemptAtUtc);
            // H26: the wait leaves its log with the voucher, its request key and the code.
            Assert.True(factory.Services.GetRequiredService<FakeArcaLogCapture>()
                .ContainsAll(waiting.Id.ToString(), order.RequestKey.ToString(), waiting.FailureCode!), waiting.FailureCode);

            factory.Clock.Advance(TimeSpan.FromSeconds(30));
            await ProcessAsync();
            Assert.Equal(ArcaVoucherState.Authorized, (await VoucherAsync(order.RequestKey)).State);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_number_someone_else_used_waits_for_a_person_and_holds_its_sequence()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var first = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(first);
            arca.Vouchers.Enqueue(FakeArcaVoucherFault.LoseRequest);
            await ProcessAsync();
            arca.Vouchers.IssueElsewhere(4, 11, 1, 20111111112, 1000m, Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            var second = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(second);

            factory.Clock.Advance(TimeSpan.FromSeconds(30));
            await ProcessAsync();
            await ProcessAsync();

            var review = await VoucherAsync(first.RequestKey);
            Assert.Equal((ArcaVoucherState.NeedsReview, (ArcaVoucherReviewReason?)ArcaVoucherReviewReason.NumberTaken), (review.State, review.ReviewReason));
            Assert.Equal(ArcaVoucherState.Queued, (await VoucherAsync(second.RequestKey)).State);
            Assert.Equal(1, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));

            var decided = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>()
                .ReviewAsync(first.RequestKey, ArcaVoucherReviewDecision.IssueWithNextNumber, review.Revision, ct));
            Assert.True(decided.IsSuccess);
            Assert.Equal((ArcaVoucherState.Queued, (long?)null), (decided.Value.State, decided.Value.Number));

            await ProcessAsync();
            await ProcessAsync();
            Assert.Equal((long?)2, (await VoucherAsync(first.RequestKey)).Number);
            Assert.Equal((long?)3, (await VoucherAsync(second.RequestKey)).Number);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_date_that_left_the_window_waits_for_a_person_and_is_issued_today()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(order);
            factory.Clock.Advance(TimeSpan.FromDays(6));

            await ProcessAsync();
            var review = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.NeedsReview, (ArcaVoucherReviewReason?)ArcaVoucherReviewReason.DateOutOfRange), (review.State, review.ReviewReason));
            Assert.Equal(0, arca.Calls("FECompUltimoAutorizado", FakeArca.InvoicingService));

            Assert.Equal(ArcaInvoicingErrors.VoucherRevisionConflict, (await factory.ExecuteScopeAsync(services => services
                .GetRequiredService<IArcaVoucherReviewService>().ReviewAsync(order.RequestKey, ArcaVoucherReviewDecision.IssueToday, review.Revision - 1, ct))).Error);
            await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>()
                .ReviewAsync(order.RequestKey, ArcaVoucherReviewDecision.IssueToday, review.Revision, ct));
            await ProcessAsync();

            var issued = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (DateOnly?)Today), (issued.State, issued.IssuedOn));
            Assert.Equal(Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture), Assert.Single(arca.Vouchers.Issued).IssuedOn);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // ADR 0031 (H1): with the number that was next, a 10016 is about the date. The voucher goes to a person with its number free,
    // the sequence goes on, and «Emitir con la fecha de hoy» issues it.
    [Fact]
    public async Task A_sale_dated_before_the_last_voucher_goes_to_review_with_its_number_free_and_is_issued_today()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            // Another system issued number 1 today: the base does not know it, so it cannot warn before queuing.
            arca.Vouchers.IssueElsewhere(4, 11, 1, 20111111112, 1000m, Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            var late = ArcaVoucherScenario.Order(Today.AddDays(-2));
            await EnqueueAsync(late);
            await ProcessAsync();
            var sent = await VoucherAsync(late.RequestKey);
            Assert.Equal((ArcaVoucherState.Uncertain, (long?)2), (sent.State, sent.Number));

            factory.Clock.Advance(ArcaRetrySchedule.After(1));
            await ProcessAsync();

            var review = await VoucherAsync(late.RequestKey);
            Assert.Equal((ArcaVoucherState.NeedsReview, (ArcaVoucherReviewReason?)ArcaVoucherReviewReason.DateBeforeLastVoucher, (long?)null),
                (review.State, review.ReviewReason, review.Number));
            Assert.Equal(1, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));

            // The sequence does not wait for the person: the next sale takes number 2.
            var next = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(next);
            await ProcessAsync();
            var issuedNext = await VoucherAsync(next.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)2), (issuedNext.State, issuedNext.Number));

            Assert.True((await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>()
                .ReviewAsync(late.RequestKey, ArcaVoucherReviewDecision.IssueToday, review.Revision, ct))).IsSuccess);
            await ProcessAsync();

            var issued = await VoucherAsync(late.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)3, (DateOnly?)Today), (issued.State, issued.Number, issued.IssuedOn));
            Assert.Equal(3, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // ADR 0031: a numbered voucher that ARCA did not issue goes again with its number only if ARCA can take its date; if its date
    // left the window, it goes to review out of the window, with its number free, instead of being sent forever.
    [Fact]
    public async Task A_numbered_voucher_whose_date_left_the_window_goes_to_review_with_its_number_free()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(order);
            arca.Vouchers.Enqueue(FakeArcaVoucherFault.LoseRequest);
            await ProcessAsync();
            factory.Clock.Advance(TimeSpan.FromDays(6));
            await ProcessAsync();

            var review = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.NeedsReview, (ArcaVoucherReviewReason?)ArcaVoucherReviewReason.DateOutOfRange, (long?)null),
                (review.State, review.ReviewReason, review.Number));
            Assert.Equal(1, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));

            await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>()
                .ReviewAsync(order.RequestKey, ArcaVoucherReviewDecision.IssueToday, review.Revision, ct));
            await ProcessAsync();

            var issued = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)1, (DateOnly?)Today), (issued.State, issued.Number, issued.IssuedOn));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // ADR 0031: «Emitir con la fecha de hoy» never goes before the last voucher the base knows: with one dated after today, it
    // takes that date, which ARCA does take.
    [Fact]
    public async Task Issuing_with_todays_date_takes_the_date_of_the_last_voucher_when_it_is_later()
    {
        var ct = TestContext.Current.CancellationToken;
        Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var old = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(old);
            factory.Clock.Advance(TimeSpan.FromDays(6));
            await ProcessAsync();
            var review = await VoucherAsync(old.RequestKey);
            Assert.Equal(ArcaVoucherReviewReason.DateOutOfRange, review.ReviewReason);

            var ahead = ArcaVoucherScenario.Order(Today.AddDays(2));
            await EnqueueAsync(ahead);
            await ProcessAsync();
            Assert.Equal(ArcaVoucherState.Authorized, (await VoucherAsync(ahead.RequestKey)).State);

            await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>()
                .ReviewAsync(old.RequestKey, ArcaVoucherReviewDecision.IssueToday, review.Revision, ct));
            await ProcessAsync();

            var issued = await VoucherAsync(old.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)2, (DateOnly?)Today.AddDays(2)), (issued.State, issued.Number, issued.IssuedOn));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // B2: «Emitir con la fecha de hoy» moves to that date a payment due date of a service that already passed; ARCA refuses a
    // due date before the date of the voucher. The period does not move.
    [Fact]
    public async Task Issuing_a_service_with_todays_date_moves_a_payment_due_date_that_passed()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var (from, to) = (Today.AddDays(-30), Today);
            var order = ArcaVoucherScenario.Order(Today) with { Concept = 2, ServiceFrom = from, ServiceTo = to, PaymentDueOn = Today.AddDays(2) };
            await EnqueueAsync(order);
            factory.Clock.Advance(TimeSpan.FromDays(11));
            await ProcessAsync();

            var review = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.NeedsReview, (ArcaVoucherReviewReason?)ArcaVoucherReviewReason.DateOutOfRange), (review.State, review.ReviewReason));
            await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>()
                .ReviewAsync(order.RequestKey, ArcaVoucherReviewDecision.IssueToday, review.Revision, ct));
            await ProcessAsync();

            Assert.Equal(ArcaVoucherState.Authorized, (await VoucherAsync(order.RequestKey)).State);
            var request = arca.LastRequest("FECAESolicitar")!;
            string Day(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            Assert.Equal((Day(Today), Day(Today), Day(from), Day(to)),
                (Value(request, "CbteFch"), Value(request, "FchVtoPago"), Value(request, "FchServDesde"), Value(request, "FchServHasta")));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // ADR 0031: a date before the last voucher the base knows would come back with 10016: «¿La emito?» and «Emitir» say so first.
    [Fact]
    public async Task A_date_before_the_last_voucher_the_base_knows_is_not_queued()
    {
        var ct = TestContext.Current.CancellationToken;
        Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var first = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(first);
            await ProcessAsync();
            Assert.Equal(ArcaVoucherState.Authorized, (await VoucherAsync(first.RequestKey)).State);

            var late = ArcaVoucherScenario.Order(Today.AddDays(-1));
            var preview = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherService>().PreviewAsync(late, ct));
            var queued = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherService>().EnqueueAsync(late, ct));

            Assert.Equal((ArcaInvoicingErrors.IssueDateBeforeLast, ArcaInvoicingErrors.IssueDateBeforeLast), (preview.Error, queued.Error));
            Assert.Empty(await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>().ListAsync([late.RequestKey], ct)));
            // The same date as the last one is fine.
            Assert.True((await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherService>()
                .PreviewAsync(ArcaVoucherScenario.Order(Today), ct))).IsSuccess);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // H16: a person cancels while the queue waits for ARCA; when ARCA fails, the queue only lets go of the sequence, which takes
    // the next voucher right away instead of two minutes later.
    [Fact]
    public async Task Cancelling_while_the_queue_waits_for_arca_does_not_hold_its_sequence()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(order);
            arca.Hold();
            var processing = ProcessAsync();
            await WaitUntilAsync(() => arca.Calls("loginCms", FakeArca.InvoicingService) > 0, ct);
            Assert.True((await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>().CancelAsync(order.RequestKey, ct))).IsSuccess);
            arca.Fail(FakeArca.InvoicingService, FakeArcaFailure.ServiceUnavailable);
            arca.Release();
            await processing;

            var next = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(next);
            await ProcessAsync();
            Assert.Equal((ArcaVoucherState.Cancelled, ArcaVoucherState.Authorized), ((await VoucherAsync(order.RequestKey)).State, (await VoucherAsync(next.RequestKey)).State));
        }
        finally
        {
            arca.Release();
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // H33: a round that asked for a CAE says so; a sequence frozen by a review is due but does not count as work.
    [Fact]
    public async Task A_round_counts_as_work_only_when_it_asked_for_a_cae()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var processor = factory.Services.GetRequiredService<ArcaVoucherProcessor>();
            var first = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(first);
            arca.Vouchers.Enqueue(FakeArcaVoucherFault.LoseRequest);
            Assert.True(await processor.ProcessDueAsync(ct));

            // Someone else used number 1: the voucher waits for a person with its number, and the next one waits behind it.
            arca.Vouchers.IssueElsewhere(4, 11, 1, 20111111112, 1000m, Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            await EnqueueAsync(ArcaVoucherScenario.Order(Today));
            factory.Clock.Advance(TimeSpan.FromSeconds(30));
            await processor.ProcessDueAsync(ct);
            Assert.Equal(ArcaVoucherState.NeedsReview, (await VoucherAsync(first.RequestKey)).State);

            Assert.False(await processor.ProcessDueAsync(ct));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // H33: the worker goes on without its pause while it asks for CAEs: three vouchers go out without the clock moving.
    [Fact]
    public async Task The_worker_does_not_pause_between_vouchers_it_issues()
    {
        var ct = TestContext.Current.CancellationToken;
        Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        var worker = new ArcaVoucherBackgroundService(factory.Services.GetRequiredService<ArcaVoucherProcessor>(),
            Microsoft.Extensions.Options.Options.Create(new ArcaVoucherWorkerOptions()), factory.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ArcaVoucherBackgroundService>.Instance);
        try
        {
            ArcaVoucherOrder[] orders = [ArcaVoucherScenario.Order(Today), ArcaVoucherScenario.Order(Today), ArcaVoucherScenario.Order(Today)];
            foreach (var order in orders) await EnqueueAsync(order);

            await worker.StartAsync(ct);
            await WaitUntilAsync(() => orders.All(order => VoucherAsync(order.RequestKey).GetAwaiter().GetResult().State == ArcaVoucherState.Authorized), ct);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task Two_hosts_never_work_the_same_sequence_at_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var first = ArcaVoucherScenario.Order(Today);
            var second = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(first);
            await EnqueueAsync(second);
            arca.Hold();

            var hostA = ProcessAsync();
            await WaitUntilAsync(() => arca.Calls("loginCms", FakeArca.InvoicingService) == 1, ct);
            await ProcessAsync();
            arca.Release();
            await hostA;

            Assert.Equal((ArcaVoucherState.Authorized, ArcaVoucherState.Queued), ((await VoucherAsync(first.RequestKey)).State, (await VoucherAsync(second.RequestKey)).State));
            await ProcessAsync();
            Assert.Equal([1L, 2L], arca.Vouchers.Issued.Select(voucher => voucher.Number));
            Assert.Equal(2, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));
        }
        finally
        {
            arca.Release();
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_host_that_died_after_saving_the_number_is_reconciled_by_the_next_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(order);
            await factory.ExecuteScopeAsync(async services =>
            {
                var store = services.GetRequiredService<IArcaVoucherStore>();
                var work = await store.ReserveAsync(ArcaVoucherScenario.Sequence(), Guid.NewGuid(), TimeSpan.FromMinutes(2), ct);
                Assert.NotNull(await store.AssignNumberAsync(work!, 0, ct));
            });

            var processor = factory.Services.GetRequiredService<ArcaVoucherProcessor>();
            await processor.ProcessDueAsync(ct);
            Assert.Equal(ArcaVoucherState.Sending, (await VoucherAsync(order.RequestKey)).State);

            factory.Clock.Advance(TimeSpan.FromMinutes(2));
            await processor.ProcessDueAsync(ct);

            var issued = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)1), (issued.State, issued.Number));
            Assert.Equal((1, 1), (arca.Calls("FECompConsultar", FakeArca.InvoicingService), arca.Calls("FECAESolicitar", FakeArca.InvoicingService)));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // H2: what a version this one does not know queued waits for the one that knows it, without being built again or showing a
    // receiver it cannot read.
    [Fact]
    public async Task A_voucher_queued_by_a_version_this_one_does_not_know_waits()
    {
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            var queued = await EnqueueAsync(order);
            await NewerVersionAsync(queued.Id);

            Assert.False(await ProcessAsync());

            var waiting = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Queued, "Arca.Invoicing.VersionUnknown", (long?)null, (ArcaVoucherParty?)null),
                (waiting.State, waiting.FailureCode, waiting.Number, waiting.Receiver));
            Assert.True(waiting.NextAttemptAtUtc > factory.Clock.GetUtcNow().UtcDateTime);
            Assert.Equal(0, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // H2: neither a review nor a credit note decides over what this version does not understand, since both write it again.
    [Fact]
    public async Task A_version_this_one_does_not_know_is_neither_reviewed_nor_cancelled_with_a_credit_note()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var invoice = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(invoice);
            await ProcessAsync();
            var issued = await VoucherAsync(invoice.RequestKey);
            Assert.Equal(ArcaVoucherState.Authorized, issued.State);
            await NewerVersionAsync(issued.Id);
            Assert.Equal("Arca.Invoicing.VersionUnknown", (await CreditNoteAsync(Guid.CreateVersion7(), invoice.RequestKey, ct)).Error.Code);

            var late = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(late);
            factory.Clock.Advance(TimeSpan.FromDays(6));
            await ProcessAsync();
            var review = await VoucherAsync(late.RequestKey);
            Assert.Equal(ArcaVoucherState.NeedsReview, review.State);
            await NewerVersionAsync(review.Id);

            var reviewed = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>()
                .ReviewAsync(late.RequestKey, ArcaVoucherReviewDecision.IssueToday, review.Revision, ct));
            Assert.Equal("Arca.Invoicing.VersionUnknown", reviewed.Error.Code);
            Assert.Equal(ArcaVoucherState.NeedsReview, (await VoucherAsync(late.RequestKey)).State);
            Assert.Equal(1, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // H2: before sending again, what would go out is checked against the hash saved with the number. If it does not match, a
    // person decides, with the number held, and the next number goes out with what was fixed when queued.
    [Fact]
    public async Task A_voucher_whose_hash_does_not_match_goes_to_review_instead_of_being_sent_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            var queued = await EnqueueAsync(order);
            await factory.ExecuteScopeAsync(async services =>
            {
                var store = services.GetRequiredService<IArcaVoucherStore>();
                var work = await store.ReserveAsync(ArcaVoucherScenario.Sequence(), Guid.NewGuid(), TimeSpan.FromMinutes(2), ct);
                Assert.NotNull(await store.AssignNumberAsync(work!, 0, ct));
            });
            await factory.ExecuteScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<ArcaVoucher>()
                .Where(voucher => voucher.Id == queued.Id).ExecuteUpdateAsync(set => set.SetProperty(voucher => voucher.DataHash, new string('0', 64)), ct));

            factory.Clock.Advance(TimeSpan.FromMinutes(2));
            Assert.False(await ProcessAsync());

            var review = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.NeedsReview, (ArcaVoucherReviewReason?)ArcaVoucherReviewReason.DataChanged, (long?)1),
                (review.State, review.ReviewReason, review.Number));
            Assert.Equal((1, 0), (arca.Calls("FECompConsultar", FakeArca.InvoicingService), arca.Calls("FECAESolicitar", FakeArca.InvoicingService)));

            var reviewed = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>()
                .ReviewAsync(order.RequestKey, ArcaVoucherReviewDecision.IssueWithNextNumber, review.Revision, ct));
            Assert.True(reviewed.IsSuccess, reviewed.IsFailure ? reviewed.Error.Code : null);
            await ProcessAsync();

            var issued = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)1, 605500m), (issued.State, issued.Number, issued.Total));
            Assert.Equal(1, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // H3: ARCA has vouchers the base does not know (a restore, or another system on the point of sale): a person decides before a
    // number is taken. Issuing goes out once, without asking again while ARCA stays where the review saw it, and asks again if
    // ARCA went on.
    [Fact]
    public async Task A_sale_numbered_when_arca_is_ahead_of_the_base_waits_for_a_person_and_goes_out_once()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 11);
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            await EnqueueAsync(ArcaVoucherScenario.Order(Today));
            await ProcessAsync();
            arca.Vouchers.SetLast(4, 11, 20);

            var ahead = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(ahead);
            Assert.False(await ProcessAsync());
            var review = await VoucherAsync(ahead.RequestKey);
            Assert.Equal((ArcaVoucherState.NeedsReview, (ArcaVoucherReviewReason?)ArcaVoucherReviewReason.ArcaAhead, (long?)null),
                (review.State, review.ReviewReason, review.Number));

            await ReviewAsync(ahead.RequestKey, ArcaVoucherReviewDecision.IssueWithNextNumber, review.Revision);
            await ProcessAsync();
            var issued = await VoucherAsync(ahead.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)21), (issued.State, issued.Number));

            var later = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(later);
            arca.Vouchers.SetLast(4, 11, 25);
            await ProcessAsync();
            await ReviewAsync(later.RequestKey, ArcaVoucherReviewDecision.IssueWithNextNumber, (await VoucherAsync(later.RequestKey)).Revision);
            arca.Vouchers.SetLast(4, 11, 26);
            await ProcessAsync();

            var again = await VoucherAsync(later.RequestKey);
            Assert.Equal((ArcaVoucherState.NeedsReview, (ArcaVoucherReviewReason?)ArcaVoucherReviewReason.ArcaAhead), (again.State, again.ReviewReason));
            Assert.Equal(2, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // H3: going on with the next number after one someone else used does not ask again, although ARCA is ahead of the base.
    [Fact]
    public async Task Going_on_after_a_number_someone_else_used_does_not_ask_again()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 11);
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            await EnqueueAsync(ArcaVoucherScenario.Order(Today));
            await ProcessAsync();
            var taken = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(taken);
            arca.Vouchers.Enqueue(FakeArcaVoucherFault.LoseRequest);
            await ProcessAsync();
            arca.Vouchers.IssueElsewhere(4, 11, 13, 20111111112, 1000m, Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

            factory.Clock.Advance(TimeSpan.FromSeconds(30));
            await ProcessAsync();
            var review = await VoucherAsync(taken.RequestKey);
            Assert.Equal((ArcaVoucherReviewReason?)ArcaVoucherReviewReason.NumberTaken, review.ReviewReason);

            await ReviewAsync(taken.RequestKey, ArcaVoucherReviewDecision.IssueWithNextNumber, review.Revision);
            await ProcessAsync();

            var issued = await VoucherAsync(taken.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)14), (issued.State, issued.Number));
            Assert.Equal(3, arca.Calls("FECAESolicitar", FakeArca.InvoicingService));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // B4: an expired certificate is cut before calling ARCA: the voucher waits for the connection, without asking for a number.
    [Fact]
    public async Task With_an_expired_certificate_the_voucher_waits_for_the_connection_without_calling_arca()
    {
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var order = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(order);
            await factory.ExecuteScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<ArcaCertificate>()
                .Where(certificate => certificate.Role == ArcaCertificateRole.Active)
                .ExecuteUpdateAsync(set => set.SetProperty(certificate => certificate.NotAfterUtc, factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1)),
                    TestContext.Current.CancellationToken));

            Assert.False(await ProcessAsync());

            var waiting = await VoucherAsync(order.RequestKey);
            Assert.Equal((ArcaVoucherState.Queued, "Arca.Connection.CertificateExpired"), (waiting.State, waiting.FailureCode));
            Assert.Equal((0, 0), (arca.Calls("FECompUltimoAutorizado", FakeArca.InvoicingService), arca.Calls("FECAESolicitar", FakeArca.InvoicingService)));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_credit_note_cancels_an_authorized_invoice_by_its_total_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var invoice = ArcaVoucherScenario.Order(Today);
            await EnqueueAsync(invoice);
            var pending = Guid.CreateVersion7();
            Assert.Equal(ArcaInvoicingErrors.InvoiceNotAuthorized, (await CreditNoteAsync(pending, invoice.RequestKey, ct)).Error);
            await ProcessAsync();

            var noteKey = Guid.CreateVersion7();
            var note = await CreditNoteAsync(noteKey, invoice.RequestKey, ct);
            Assert.Equal((ArcaVoucherKind.CreditNote, 13, 605500m), (note.Value.Kind, note.Value.VoucherType, note.Value.Total));
            Assert.Equal(ArcaInvoicingErrors.InvoiceAlreadyCancelled, (await CreditNoteAsync(Guid.CreateVersion7(), invoice.RequestKey, ct)).Error);

            await ProcessAsync(13);

            var authorizedNote = await VoucherAsync(noteKey);
            Assert.Equal((ArcaVoucherState.Authorized, (long?)1, invoice.Receiver), (authorizedNote.State, authorizedNote.Number, authorizedNote.Receiver));
            var request = arca.LastRequest("FECAESolicitar")!;
            Assert.Equal(["11", "4", "1"], request.Descendants(Fev1 + "CbteAsoc").Single().Elements().Take(3).Select(element => element.Value));
            Assert.Equal("605500.00", request.Descendants(Fev1 + "ImpTotal").Single().Value);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_registered_taxpayer_issues_class_a_with_the_vat_of_each_rate_and_cancels_it_with_a_class_a_note()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority, ArcaVatCondition.RegisteredTaxpayer);
        try
        {
            var order = ArcaVoucherScenario.Order(Today) with
            {
                Items =
                [
                    new ArcaVoucherOrderItem("Semilla de soja", 10m, 52000m, new(ArcaVatTreatmentKind.Taxed, 5)),
                    new ArcaVoucherOrderItem("Flete", 1m, 85500m, new(ArcaVatTreatmentKind.Taxed, 4)),
                ],
                // The form's «Total» is the sum of the items, without the VAT that the A adds.
                ExpectedTotal = 605500m,
            };
            var preview = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherService>().PreviewAsync(order, ct));
            Assert.Equal((ArcaVoucherClass.A, 1, 723677.5m, 118177.5m), (preview.Value.Class, preview.Value.VoucherType, preview.Value.Total, preview.Value.Vat));
            Assert.Equal(605500m, preview.Value.Amounts!.Net);

            var queued = await EnqueueAsync(order);
            Assert.Equal((ArcaVoucherClass.A, 1, 723677.5m), (queued.Class, queued.VoucherType, queued.Total));
            await ProcessAsync(1);

            Assert.Equal(ArcaVoucherState.Authorized, (await VoucherAsync(order.RequestKey)).State);
            var invoice = arca.LastRequest("FECAESolicitar")!;
            Assert.Equal(("80", "1", "605500.00", "118177.50", "723677.50"),
                (Value(invoice, "DocTipo"), Value(invoice, "CondicionIVAReceptorId"), Value(invoice, "ImpNeto"), Value(invoice, "ImpIVA"),
                 Value(invoice, "ImpTotal")));
            Assert.Equal(["4|85500.00|8977.50", "5|520000.00|109200.00"], VatRates(invoice));

            var noteKey = Guid.CreateVersion7();
            var note = await CreditNoteAsync(noteKey, order.RequestKey, ct);
            Assert.Equal((ArcaVoucherClass.A, 3, 723677.5m), (note.Value.Class, note.Value.VoucherType, note.Value.Total));
            await ProcessAsync(3);

            Assert.Equal(ArcaVoucherState.Authorized, (await VoucherAsync(noteKey)).State);
            var credit = arca.LastRequest("FECAESolicitar")!;
            Assert.Equal(["1", "4", "1"], credit.Descendants(Fev1 + "CbteAsoc").Single().Elements().Take(3).Select(element => element.Value));
            Assert.Equal(VatRates(invoice), VatRates(credit));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_registered_taxpayer_issues_class_b_to_a_final_consumer_with_prices_with_vat_and_an_exempt_item()
    {
        var arca = Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority, ArcaVatCondition.RegisteredTaxpayer);
        try
        {
            var order = ArcaVoucherScenario.Order(Today) with
            {
                Receiver = new ArcaVoucherParty(99, 0, "Consumidor final", 5, null),
                Items =
                [
                    new ArcaVoucherOrderItem("Bolsa de alimento", 2m, 12100m, new(ArcaVatTreatmentKind.Taxed, 5)),
                    new ArcaVoucherOrderItem("Libro", 1m, 15000m, new(ArcaVatTreatmentKind.Exempt)),
                ],
                ExpectedTotal = 39200m,
                PricesIncludeVat = true,
            };

            var queued = await EnqueueAsync(order);
            Assert.Equal((ArcaVoucherClass.B, 6, 39200m), (queued.Class, queued.VoucherType, queued.Total));
            await ProcessAsync(6);

            Assert.Equal(ArcaVoucherState.Authorized, (await VoucherAsync(order.RequestKey)).State);
            var request = arca.LastRequest("FECAESolicitar")!;
            Assert.Equal(("99", "5", "20000.00", "4200.00", "15000.00", "0.00", "39200.00"),
                (Value(request, "DocTipo"), Value(request, "CondicionIVAReceptorId"), Value(request, "ImpNeto"), Value(request, "ImpIVA"),
                 Value(request, "ImpOpEx"), Value(request, "ImpTotConc"), Value(request, "ImpTotal")));
            Assert.Equal(["5|20000.00|4200.00"], VatRates(request));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_class_a_order_without_the_vat_of_its_items_is_not_queued()
    {
        var ct = TestContext.Current.CancellationToken;
        Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority, ArcaVatCondition.RegisteredTaxpayer);
        try
        {
            var result = await factory.ExecuteScopeAsync(services =>
                services.GetRequiredService<IArcaVoucherService>().EnqueueAsync(ArcaVoucherScenario.Order(Today), ct));

            Assert.Equal(ArcaInvoicingErrors.ItemsInvalid, result.Error);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    public void Dispose() => authority.Dispose();

    private DateOnly Today => ArcaIssueDateWindow.Today(factory.Clock.GetUtcNow().UtcDateTime);

    private async Task<ArcaVoucherResponse> EnqueueAsync(ArcaVoucherOrder order)
    {
        var result = await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IArcaVoucherService>().EnqueueAsync(order, TestContext.Current.CancellationToken));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private Task<Domain.Results.Result<ArcaVoucherResponse>> CreditNoteAsync(Guid requestKey, Guid invoiceKey, CancellationToken ct) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherService>().EnqueueCreditNoteAsync(requestKey, invoiceKey, ct));

    private async Task ReviewAsync(Guid requestKey, ArcaVoucherReviewDecision decision, int revision)
    {
        var reviewed = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherReviewService>()
            .ReviewAsync(requestKey, decision, revision, TestContext.Current.CancellationToken));
        Assert.True(reviewed.IsSuccess, reviewed.IsFailure ? reviewed.Error.Code : null);
    }

    private async Task<ArcaVoucherResponse> VoucherAsync(Guid requestKey) =>
        Assert.Single(await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IArcaVoucherReviewService>().ListAsync([requestKey], TestContext.Current.CancellationToken)));

    // As a later version of the app would leave it: the same voucher, fixed with a form this one does not know.
    private Task NewerVersionAsync(Guid voucherId) => factory.ExecuteScopeAsync(async services =>
    {
        var vouchers = services.GetRequiredService<ApplicationDbContext>().Set<ArcaVoucher>();
        var payload = await vouchers.Where(voucher => voucher.Id == voucherId).Select(voucher => voucher.PayloadJson).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"version\": 1", payload, StringComparison.Ordinal);
        await vouchers.Where(voucher => voucher.Id == voucherId).ExecuteUpdateAsync(set => set.SetProperty(voucher => voucher.PayloadJson,
            payload.Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal)), TestContext.Current.CancellationToken);
    });

    private static string Value(XElement request, string name) => request.Descendants(Fev1 + name).Single().Value;

    private static string[] VatRates(XElement request) => [.. request.Descendants(Fev1 + "AlicIva").Select(rate =>
        $"{rate.Element(Fev1 + "Id")!.Value}|{rate.Element(Fev1 + "BaseImp")!.Value}|{rate.Element(Fev1 + "Importe")!.Value}")];

    private Task<bool> ProcessAsync(int voucherType = 11) =>
        factory.Services.GetRequiredService<ArcaVoucherProcessor>().ProcessAsync(ArcaVoucherScenario.Sequence(voucherType), TestContext.Current.CancellationToken);

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }

    private FakeArca Arca()
    {
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        return arca;
    }
}

/// <summary>Las fallas de <c>FECAESolicitar</c> que dejan el comprobante sin emitir, como dato público de la teoría.</summary>
public enum FakeArcaVoucherFaultCase
{
    LoseRequest,
    TransactionActive,
    InternalError,
}
