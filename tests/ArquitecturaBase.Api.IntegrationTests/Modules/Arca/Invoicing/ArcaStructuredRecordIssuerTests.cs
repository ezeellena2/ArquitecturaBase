using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>
/// ARCA como emisor de la acción de formularios, con PostgreSQL y <c>FakeArca</c>: los datos atados de una «Factura» se encolan
/// una vez por run, siguen la cola y salen emitidos con su resultado y su PDF fiscal; un pedido sin cliente vuelve rechazado.
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
    public async Task A_customer_written_by_its_cuit_is_completed_from_the_registry_before_the_preview()
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
