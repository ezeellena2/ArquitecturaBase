using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Registry;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>
/// ARCA como emisor de la acción de formularios, con PostgreSQL y <c>FakeArca</c>: los datos atados de una «Factura» se encolan
/// una vez por run, siguen la cola y salen emitidos con su resultado (también la fecha y el cliente con su documento y su condición
/// frente al IVA) y su PDF fiscal; un pedido sin cliente vuelve rechazado. La
/// «Razón social» de «Clientes» llega entera al comprobante aunque el texto de la opción esté recortado.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaStructuredRecordIssuerTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    [Fact]
    public async Task A_bound_invoice_is_queued_once_by_run_and_comes_out_issued_with_its_result_and_fiscal_pdf()
    {
        var ct = TestContext.Current.CancellationToken;
        factory.Services.GetRequiredService<FakeArca>().Reset();
        var files = new ArcaMemoryFiles();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(files);
        }));
        host.Services.GetRequiredService<FakeArca>().Reset();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var request = Request(Guid.CreateVersion7());
            var queued = await IssuerAsync(host, issuer => issuer.EnqueueAsync(request, ct));
            var again = await IssuerAsync(host, issuer => issuer.EnqueueAsync(request, ct));
            Assert.Equal((StructuredIssueState.Pending, StructuredIssueState.Pending), (queued.State, again.State));

            var processor = host.Services.GetRequiredService<ArcaVoucherProcessor>();
            await processor.ProcessAsync(ArcaVoucherScenario.Sequence(), ct);
            // H7: authorized before its PDF, it is issued with its result, and the run waits for the document.
            var beforePdf = await IssuerAsync(host, issuer => issuer.GetAsync(request.RunId, ct));
            Assert.Equal((StructuredIssueState.Issued, true, (StructuredRecordActionDocument?)null), (beforePdf.State, beforePdf.DocumentPending, beforePdf.Document));
            await processor.ProcessDuePdfsAsync(ct);

            var issued = await IssuerAsync(host, issuer => issuer.GetAsync(request.RunId, ct));
            Assert.Equal(StructuredIssueState.Issued, issued.State);
            Assert.Equal(["Factura C", "00004-00000001"], issued.Result!.Values.Take(2).Select(value => value.Value));
            Assert.Equal("1300.5", issued.Result.Values.Single(value => value.Key == "total").Value);
            // The zone «Comprobante» of the detail: the date, the customer, its document and its VAT condition, as the fiscal PDF says them.
            var values = issued.Result.Values.ToDictionary(value => value.Key, value => value.Value);
            Assert.Equal((ArcaIssueDateWindow.Today(factory.Clock.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "Agro del Sur S.A.", "CUIT 30-71234567-1", "IVA Responsable Inscripto"),
                (values["issuedOn"], values["customer"], values["customerDocument"], values["customerVatCondition"]));
            Assert.True(files.Objects.ContainsKey(issued.Document!.StorageKey));
            Assert.Equal("Factura C 00004-00000001.pdf", issued.Document.Name);
            Assert.Equal(1, host.Services.GetRequiredService<FakeArca>().Calls("FECAESolicitar", FakeArca.InvoicingService));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // The band of the zone «Comprobante»: «Rechazada» with the code and text ARCA sent, and «En cola» with when the queue tries
    // again and how many times it tried.
    // H7: with the store down after ARCA authorized, the invoice is issued with its number and CAE, the notice does not blame
    // ARCA and the PDF waits with its own code.
    [Fact]
    public async Task With_the_store_down_after_authorizing_the_invoice_is_issued_with_its_cae_and_its_pdf_waits()
    {
        var ct = TestContext.Current.CancellationToken;
        var files = new ArcaMemoryFiles { Available = false };
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(files);
        }));
        host.Services.GetRequiredService<FakeArca>().Reset();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var request = Request(Guid.CreateVersion7());
            await IssuerAsync(host, issuer => issuer.EnqueueAsync(request, ct));
            var processor = host.Services.GetRequiredService<ArcaVoucherProcessor>();
            await processor.ProcessAsync(ArcaVoucherScenario.Sequence(), ct);
            await processor.ProcessDuePdfsAsync(ct);

            var issued = await IssuerAsync(host, issuer => issuer.GetAsync(request.RunId, ct));
            Assert.Equal((StructuredIssueState.Issued, true, (IReadOnlyDictionary<string, string>?)null), (issued.State, issued.DocumentPending, issued.WaitingNotice));
            Assert.Equal(host.Services.GetRequiredService<FakeArca>().Vouchers.Issued.Single().Cae, issued.Result!.Values.Single(value => value.Key == "cae").Value);
            Assert.Equal("Arca.Invoicing.PdfStorageUnavailable", (await host.Services.CreateScope().ServiceProvider.GetRequiredService<IArcaVoucherReviewService>()
                .ListAsync([request.RunId], ct)).Single().FailureCode);
            Assert.Empty(files.Objects);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_rejection_comes_back_with_what_arca_said_and_a_wait_with_how_many_times_the_queue_tried()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var processor = factory.Services.GetRequiredService<ArcaVoucherProcessor>();
            var rejected = Request(Guid.CreateVersion7());
            await IssuerAsync(factory, issuer => issuer.EnqueueAsync(rejected, ct));
            arca.Vouchers.Enqueue(FakeArcaVoucherFault.Reject);
            await processor.ProcessAsync(ArcaVoucherScenario.Sequence(), ct);

            var refused = await IssuerAsync(factory, issuer => issuer.GetAsync(rejected.RunId, ct));
            Assert.Equal((StructuredIssueState.Rejected, "wsfe.10015", "10015 · El numero de documento del receptor no es valido"),
                (refused.State, refused.FailureCode, refused.FailureDetail));
            Assert.Null(refused.Attempts);

            var waiting = Request(Guid.CreateVersion7());
            await IssuerAsync(factory, issuer => issuer.EnqueueAsync(waiting, ct));
            arca.Fail(FakeArca.InvoicingService, FakeArcaFailure.ServiceUnavailable);
            await processor.ProcessAsync(ArcaVoucherScenario.Sequence(), ct);
            factory.Clock.Advance(ArcaRetrySchedule.After(1));
            arca.Fail(FakeArca.InvoicingService, FakeArcaFailure.ServiceUnavailable);
            await processor.ProcessAsync(ArcaVoucherScenario.Sequence(), ct);

            var queued = await IssuerAsync(factory, issuer => issuer.GetAsync(waiting.RunId, ct));
            Assert.Equal((StructuredIssueState.Pending, true, 2, factory.Clock.GetUtcNow().UtcDateTime + ArcaRetrySchedule.After(2)),
                (queued.State, queued.WaitingForProvider, queued.Attempts, queued.NextCheckAtUtc));
            Assert.Null(queued.FailureDetail);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // «Factura» comes with «El precio incluye IVA» unmarked: the form's «Total» is the sum of the items, without VAT. A registered
    // taxpayer issues its A and its B that way and with the box marked, when the sum already has the VAT.
    [Theory]
    [InlineData(false, false, ArcaVoucherClass.A, 1, 1573.6)]
    [InlineData(false, true, ArcaVoucherClass.A, 1, 1300.5)]
    [InlineData(true, false, ArcaVoucherClass.B, 6, 1573.6)]
    [InlineData(true, true, ArcaVoucherClass.B, 6, 1300.5)]
    public async Task A_registered_taxpayer_issues_the_a_and_the_b_of_the_standard_with_or_without_the_vat_in_its_prices(bool finalConsumer,
        bool pricesIncludeVat, ArcaVoucherClass voucherClass, int voucherType, double total)
    {
        var ct = TestContext.Current.CancellationToken;
        factory.Services.GetRequiredService<FakeArca>().Reset();
        await ArcaVoucherScenario.ConnectAsync(factory, authority, ArcaVatCondition.RegisteredTaxpayer);
        try
        {
            var request = Request(Guid.CreateVersion7());
            StructuredIssueDatum[] data =
            [
                finalConsumer ? new("customer", "final", "Consumidor final") : request.Data[0],
                .. request.Data.Skip(1),
                new("pricesIncludeVat", pricesIncludeVat ? "true" : "false"),
            ];

            var queued = await IssuerAsync(factory, issuer => issuer.EnqueueAsync(request with { Data = data }, ct));
            Assert.Equal((StructuredIssueState.Pending, (string?)null), (queued.State, queued.FailureCode));
            await factory.Services.GetRequiredService<ArcaVoucherProcessor>().ProcessAsync(ArcaVoucherScenario.Sequence(voucherType), ct);

            var voucher = Assert.Single(await factory.ExecuteScopeAsync(services =>
                services.GetRequiredService<IArcaVoucherReviewService>().ListAsync([request.RunId], ct)));
            Assert.Equal((ArcaVoucherState.Authorized, voucherClass, (decimal)total), (voucher.State, voucher.Class, voucher.Total));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    // H10: with the registry not answering at «Emitir», nothing was queued, so the action can be retried.
    [Fact]
    public async Task A_registry_that_does_not_answer_at_issuing_leaves_a_rejection_that_can_be_retried()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            arca.Fail(FakeArca.RegistryService, FakeArcaFailure.ServiceUnavailable);
            var request = Request(Guid.CreateVersion7());
            StructuredIssueDatum[] data = [new("customer", "other", "Otro: escribir la CUIT"), new("customerTaxId", "20-12345678-6"), .. request.Data.Skip(1)];

            var refused = await IssuerAsync(factory, issuer => issuer.EnqueueAsync(request with { Data = data }, ct));

            Assert.Equal((StructuredIssueState.Rejected, true), (refused.State, refused.CanRetry));
            Assert.StartsWith("Arca.", refused.FailureCode, StringComparison.Ordinal);
            Assert.Equal(StructuredIssueState.NotFound, (await IssuerAsync(factory, issuer => issuer.GetAsync(request.RunId, ct))).State);

            // A CUIT that is missing is a malformed order: retrying it gives the same.
            StructuredIssueDatum[] withoutCuit = [new("customer", "other", "Otro: escribir la CUIT"), .. request.Data.Skip(1)];
            var malformed = await IssuerAsync(factory, issuer => issuer.EnqueueAsync(new StructuredIssueRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), withoutCuit), ct));
            Assert.Equal(("Arca.Invoicing.ReceiverInvalid", false), (malformed.FailureCode, malformed.CanRetry));
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_request_without_a_customer_is_rejected_and_nothing_is_queued()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var request = Request(Guid.CreateVersion7());
            var refused = await IssuerAsync(factory, issuer => issuer.EnqueueAsync(request with { Data = request.Data.Skip(1).ToArray() }, ct));

            Assert.Equal((StructuredIssueState.Rejected, "Arca.Invoicing.ReceiverInvalid"), (refused.State, refused.FailureCode));
            Assert.Equal(StructuredIssueState.NotFound, (await IssuerAsync(factory, issuer => issuer.GetAsync(request.RunId, ct))).State);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task A_customer_written_by_its_cuit_is_completed_from_the_registry_before_the_preview_and_comes_back_when_queued()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            arca.Person = new FakeArcaPerson("Lácteos del Oeste SRL", null, null, "Ruta 5 km 30", "Trenque Lauquen", "BUENOS AIRES", [30], false, []);
            var request = Request(Guid.CreateVersion7());
            StructuredIssueDatum[] data = [new("customer", "other", "Otro: escribir la CUIT"), new("customerTaxId", "30-50001091-2"), .. request.Data.Skip(1)];

            var preview = await PreviewAsync(data);
            Assert.Equal("Lácteos del Oeste SRL", preview.Receiver);
            Assert.Null(preview.FailureCode);
            var withoutCuit = await PreviewAsync([new("customer", "other", "Otro: escribir la CUIT"), .. request.Data.Skip(1)]);
            Assert.Equal("Arca.Invoicing.ReceiverInvalid", withoutCuit.FailureCode);

            // H31: «Emitir» queues it without the customer; it comes back for «Clientes» once issued, by its CUIT.
            var queued = await IssuerAsync(factory, issuer => issuer.EnqueueAsync(request with { Data = data }, ct));
            Assert.Equal((StructuredIssueState.Pending, (StructuredIssueCatalogItem?)null), (queued.State, queued.CatalogItem));
            await factory.Services.GetRequiredService<ArcaVoucherProcessor>().ProcessAsync(ArcaVoucherScenario.Sequence(), ct);
            var issued = await IssuerAsync(factory, issuer => issuer.GetAsync(request.RunId, ct));
            Assert.Equal(StructuredIssueState.Issued, issued.State);
            var customer = Assert.IsType<StructuredIssueCatalogItem>(issued.CatalogItem);
            Assert.Equal(("arca.customers", "30500010912", "Lácteos del Oeste SRL", "tax_id"), (customer.CatalogKey, customer.Code, customer.Text, customer.TaxIdField));
            Assert.Equal<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>
            {
                ["tax_id"] = "30500010912", ["legal_name"] = "Lácteos del Oeste SRL", ["vat_condition"] = "1", ["address"] = "Ruta 5 km 30, Trenque Lauquen, BUENOS AIRES",
            }, customer.Fields);
            // H31: a human person goes without the address, as the registry says by its kind of person.
            arca.Person = new FakeArcaPerson(null, "Lucía", "Gómez", "Belgrano 120", "Pergamino", "BUENOS AIRES", [30], false, []);
            var person = Request(Guid.CreateVersion7());
            await IssuerAsync(factory, issuer => issuer.EnqueueAsync(person with { Data = [new("customer", "other", "Otro: escribir la CUIT"),
                new("customerTaxId", "27287654338"), .. person.Data.Skip(1)] }, ct));
            await factory.Services.GetRequiredService<ArcaVoucherProcessor>().ProcessAsync(ArcaVoucherScenario.Sequence(), ct);
            var human = Assert.IsType<StructuredIssueCatalogItem>((await IssuerAsync(factory, issuer => issuer.GetAsync(person.RunId, ct))).CatalogItem);
            Assert.False(human.Fields.ContainsKey("address"));
        }
        finally
        {
            arca.Person = FakeArca.Company;
            await ArcaVoucherScenario.RemoveAsync(factory);
        }

        async Task<StructuredIssuePreview> PreviewAsync(IReadOnlyList<StructuredIssueDatum> data)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetServices<IStructuredRecordIssuer>().Single(issuer => issuer.IssuerId == "arca.invoice").PreviewAsync(data, ct);
        }
    }

    [Fact]
    public async Task A_long_legal_name_reaches_the_stored_voucher_and_its_fiscal_document_whole_and_comes_back_whole_for_the_customers()
    {
        const string legalName = "Cooperativa Agrícola Ganadera y de Consumo Limitada del Sur de Santa Fe";
        var ct = TestContext.Current.CancellationToken;
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            // A customer of «Clientes» whose option text was cut at 30 characters: the receiver is its «Razón social».
            var request = Request(Guid.CreateVersion7());
            StructuredIssueDatum[] data =
            [
                new("customer", Guid.NewGuid().ToString("D"), legalName[..30].TrimEnd(), new Dictionary<string, string>
                {
                    ["tax_id"] = "30-71234567-1", ["legal_name"] = legalName, ["vat_condition"] = "1", ["address"] = "Ruta 8 km 225",
                }),
                .. request.Data.Skip(1),
            ];
            Assert.Equal(StructuredIssueState.Pending, (await IssuerAsync(factory, issuer => issuer.EnqueueAsync(request with { Data = data }, ct))).State);
            await factory.Services.GetRequiredService<ArcaVoucherProcessor>().ProcessAsync(ArcaVoucherScenario.Sequence(), ct);

            var payload = await factory.ExecuteDbContextAsync(db =>
                db.Set<ArcaVoucher>().Where(row => row.RequestKey == request.RunId).Select(row => row.PayloadJson).SingleAsync(ct));
            Assert.Equal(legalName, ArcaVoucherRequestPolicy.Read(payload)!.Receiver.Name);
            var voucher = Assert.Single(await factory.ExecuteScopeAsync(services =>
                services.GetRequiredService<IArcaVoucherReviewService>().ListAsync([request.RunId], ct)));
            Assert.Equal(ArcaVoucherState.Authorized, voucher.State);
            var fiscal = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherStore>().FindPdfAsync(voucher.Id, ct));
            Assert.Equal(legalName, fiscal!.Document.ReceiverName);

            // «Otro»: the legal name the registry gave comes back whole, also in its field, for «Clientes» to keep it.
            arca.Person = new FakeArcaPerson(legalName, null, null, "Ruta 5 km 30", "Trenque Lauquen", "BUENOS AIRES", [30], false, []);
            StructuredIssueDatum[] other = [new("customer", "other", "Otro: escribir la CUIT"), new("customerTaxId", "30-50001091-2"), .. request.Data.Skip(1)];
            // H31: it comes back with the issued invoice.
            var otherRun = new StructuredIssueRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), other);
            await IssuerAsync(factory, issuer => issuer.EnqueueAsync(otherRun, ct));
            await factory.Services.GetRequiredService<ArcaVoucherProcessor>().ProcessAsync(ArcaVoucherScenario.Sequence(), ct);
            var customer = Assert.IsType<StructuredIssueCatalogItem>((await IssuerAsync(factory, issuer => issuer.GetAsync(otherRun.RunId, ct))).CatalogItem);
            Assert.Equal((legalName, legalName), (customer.Text, customer.Fields["legal_name"]));
        }
        finally
        {
            arca.Person = FakeArca.Company;
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    public void Dispose() => authority.Dispose();

    private static async Task<StructuredIssueResponse> IssuerAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,
        Func<IStructuredRecordIssuer, Task<StructuredIssueResponse>> work)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetServices<IStructuredRecordIssuer>().Single(issuer => issuer.IssuerId == "arca.invoice"));
    }

    private static StructuredIssueRequest Request(Guid runId) => new(runId, Guid.CreateVersion7(),
    [
        new("customer", Guid.NewGuid().ToString("D"), "Agro del Sur S.A.",
            new Dictionary<string, string> { ["tax_id"] = "30-71234567-1", ["vat_condition"] = "1", ["address"] = "Ruta 8 km 225" }),
        new("concept", "1", "Productos"),
        new("items", null, Items:
        [
            [new("description", "Semilla de soja"), new("quantity", "2"), new("unitPrice", "600.25")],
            [new("description", "Flete"), new("quantity", "1"), new("unitPrice", "100")],
        ]),
        new("total", "1300.5"),
    ]);
}
