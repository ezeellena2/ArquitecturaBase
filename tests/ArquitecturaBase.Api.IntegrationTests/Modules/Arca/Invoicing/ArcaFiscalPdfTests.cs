using System.Security.Cryptography;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>
/// El PDF fiscal de un comprobante autorizado con PostgreSQL: se escribe una sola vez, se confirma su SHA-256 y no se limpia
/// mientras exista el comprobante. Sin almacén, el comprobante sigue autorizado y el PDF espera.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaFiscalPdfTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    [Fact]
    public async Task An_authorized_voucher_gets_its_fiscal_pdf_once_and_keeps_it()
    {
        var ct = TestContext.Current.CancellationToken;
        Arca();
        var files = new ArcaMemoryFiles();
        await using var withFiles = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(files);
        }));
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var voucher = await AuthorizedAsync();
            var processor = withFiles.Services.GetRequiredService<ArcaVoucherProcessor>();

            await processor.ProcessDuePdfsAsync(ct);
            await processor.ProcessPdfAsync(voucher.Id, ct);

            var row = await VoucherAsync(voucher.RequestKey);
            Assert.Equal($"arca-vouchers/{voucher.Id:N}.pdf", row.PdfStorageKey);
            var stored = files.Objects[row.PdfStorageKey!];
            Assert.Equal((Convert.ToHexStringLower(SHA256.HashData(stored)), (long?)stored.Length), (row.PdfSha256, row.PdfSize));
            Assert.Equal(1, files.Writes);

            await using var scope = withFiles.Services.CreateAsyncScope();
            var retention = scope.ServiceProvider.GetServices<IPrivateFileRetentionSource>();
            var referenced = new List<string>();
            foreach (var source in retention)
                referenced.AddRange(await source.ListReferencedKeysAsync([row.PdfStorageKey!, $"arca-vouchers/{Guid.CreateVersion7():N}.pdf", "otro/archivo.pdf"], ct));
            Assert.Equal([row.PdfStorageKey!], referenced);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task Without_a_file_store_the_pdf_waits_and_the_voucher_stays_authorized()
    {
        var ct = TestContext.Current.CancellationToken;
        Arca();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var voucher = await AuthorizedAsync();

            await factory.Services.GetRequiredService<ArcaVoucherProcessor>().ProcessPdfAsync(voucher.Id, ct);

            var row = await VoucherAsync(voucher.RequestKey);
            Assert.Equal((ArcaVoucherState.Authorized, (string?)null, "Files.Unavailable"), (row.State, row.PdfStorageKey, row.FailureCode));
            Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime.AddSeconds(30), row.NextAttemptAtUtc);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    public void Dispose() => authority.Dispose();

    private async Task<ArcaVoucherResponse> AuthorizedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var order = ArcaVoucherScenario.Order(ArcaIssueDateWindow.Today(factory.Clock.GetUtcNow().UtcDateTime));
        var queued = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaVoucherService>().EnqueueAsync(order, ct));
        await factory.Services.GetRequiredService<ArcaVoucherProcessor>().ProcessAsync(ArcaVoucherScenario.Sequence(), ct);
        var authorized = await VoucherAsync(order.RequestKey);
        Assert.Equal(ArcaVoucherState.Authorized, authorized.State);
        return authorized with { Id = queued.Value.Id };
    }

    private async Task<ArcaVoucherResponse> VoucherAsync(Guid requestKey) =>
        Assert.Single(await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IArcaVoucherReviewService>().ListAsync([requestKey], TestContext.Current.CancellationToken)));

    private void Arca() => factory.Services.GetRequiredService<FakeArca>().Reset();
}
