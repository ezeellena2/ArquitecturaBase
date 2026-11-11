using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
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
            Assert.Equal(StructuredIssueState.Pending, (await IssuerAsync(host, issuer => issuer.GetAsync(request.RunId, ct))).State);
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

            // «Emitir»: queued, the customer comes back completed for «Clientes», by its CUIT; one of the catalog does not.
            var queued = await IssuerAsync(factory, issuer => issuer.EnqueueAsync(request with { Data = data }, ct));
            Assert.Equal(StructuredIssueState.Pending, queued.State);
            var customer = Assert.IsType<StructuredIssueCatalogItem>(queued.CatalogItem);
            Assert.Equal(("arca.customers", "30500010912", "Lácteos del Oeste SRL", "tax_id"), (customer.CatalogKey, customer.Code, customer.Text, customer.TaxIdField));
            Assert.Equal<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>
            {
                ["tax_id"] = "30500010912", ["legal_name"] = "Lácteos del Oeste SRL", ["vat_condition"] = "1", ["address"] = "Ruta 5 km 30, Trenque Lauquen, BUENOS AIRES",
            }, customer.Fields);
            Assert.Null((await IssuerAsync(factory, issuer => issuer.EnqueueAsync(Request(Guid.CreateVersion7()), ct))).CatalogItem);
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
            Assert.Equal(legalName, ArcaVoucherRequestPolicy.FromJson(payload).Receiver.Name);
            var voucher = Assert.Single(await factory.ExecuteScopeAsync(services =>
                services.GetRequiredService<IArcaVoucherReviewService>().ListAsync([request.RunId], ct)));
            Assert.Equal(ArcaVoucherState.Authorized, voucher.State);
            var fiscal = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherStore>().FindPdfAsync(voucher.Id, ct));
            Assert.Equal(legalName, fiscal!.Document.ReceiverName);

            // «Otro»: the legal name the registry gave comes back whole, also in its field, for «Clientes» to keep it.
            arca.Person = new FakeArcaPerson(legalName, null, null, "Ruta 5 km 30", "Trenque Lauquen", "BUENOS AIRES", [30], false, []);
            StructuredIssueDatum[] other = [new("customer", "other", "Otro: escribir la CUIT"), new("customerTaxId", "30-50001091-2"), .. request.Data.Skip(1)];
            var queued = await IssuerAsync(factory, issuer => issuer.EnqueueAsync(new StructuredIssueRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), other), ct));
            var customer = Assert.IsType<StructuredIssueCatalogItem>(queued.CatalogItem);
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
