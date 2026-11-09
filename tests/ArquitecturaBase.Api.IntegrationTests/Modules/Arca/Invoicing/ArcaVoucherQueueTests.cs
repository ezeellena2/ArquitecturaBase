using System.Globalization;
using System.Xml.Linq;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Registry;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;
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

            Assert.Equal((ArcaVoucherState.Authorized, (long?)1), ((await VoucherAsync(noteKey)).State, (await VoucherAsync(noteKey)).Number));
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
    public async Task A_registered_taxpayer_cannot_queue_until_classes_a_and_b()
    {
        var ct = TestContext.Current.CancellationToken;
        Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority, ArcaVatCondition.RegisteredTaxpayer);
        try
        {
            var result = await factory.ExecuteScopeAsync(services =>
                services.GetRequiredService<IArcaVoucherService>().EnqueueAsync(ArcaVoucherScenario.Order(Today), ct));

            Assert.Equal(ArcaInvoicingErrors.ClassNotSupported, result.Error);
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

    private async Task<ArcaVoucherResponse> VoucherAsync(Guid requestKey) =>
        Assert.Single(await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IArcaVoucherReviewService>().ListAsync([requestKey], TestContext.Current.CancellationToken)));

    private Task ProcessAsync(int voucherType = 11) =>
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
