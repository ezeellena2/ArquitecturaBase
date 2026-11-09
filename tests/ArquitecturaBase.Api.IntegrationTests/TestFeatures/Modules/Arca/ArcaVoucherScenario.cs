using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Registry;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

/// <summary>
/// Una conexión lista para emitir: certificado activo de la CA de prueba, punto de venta 4 con los datos del comprobante y
/// la empresa monotributista que trajo el padrón. Los comprobantes no dependen de la conexión: al terminar se borran por la
/// CUIT de la conexión de prueba, junto con sus secuencias.
/// </summary>
internal static class ArcaVoucherScenario
{
    public const int PointOfSale = 4;

    public static async Task<ArcaCertificateRef> ConnectAsync(ApiFactory factory, FakeArcaCertificateAuthority authority,
        ArcaVatCondition condition = ArcaVatCondition.Monotax)
    {
        await RemoveVouchersAsync(factory);
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        await factory.InTransactionAsync(services =>
        {
            var settings = ArcaInvoicingSettings.Create(certificate.ConnectionId);
            settings.ChoosePointOfSale(PointOfSale);
            settings.SetVoucherData("27-28765433-8", new DateOnly(2019, 3, 1));
            services.GetRequiredService<IArcaInvoicingRepository>().Add(settings);
            services.GetRequiredService<IArcaRegistryRepository>().Add(
                ArcaRegistryCompany.Create(certificate.ConnectionId, "Lucía Fernández", condition, "Av. San Martín 1250, Pergamino"));
            return Task.CompletedTask;
        });
        return certificate;
    }

    public static ArcaVoucherOrder Order(DateOnly issueOn, Guid? requestKey = null, long documentNumber = 30712345671) => new(
        requestKey ?? Guid.CreateVersion7(), new ArcaVoucherParty(80, documentNumber, "Agro del Sur S.A.", 1, "Ruta 8 km 225"), 1, issueOn, null, null, null,
        [new ArcaVoucherOrderItem("Semilla de soja", 10m, 52000m), new ArcaVoucherOrderItem("Flete", 1m, 85500m)], 605500m);

    public static ArcaVoucherSequenceRow Sequence(int voucherType = 11) =>
        new(ArcaEnvironment.Testing, ArcaPersistenceScenario.Cuit, PointOfSale, voucherType);

    public static async Task RemoveAsync(ApiFactory factory)
    {
        await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        await RemoveVouchersAsync(factory);
    }

    private static Task RemoveVouchersAsync(ApiFactory factory) => factory.ExecuteScopeAsync(async services =>
    {
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        await dbContext.Set<ArcaVoucher>().Where(x => x.Cuit == ArcaPersistenceScenario.Cuit && x.CancelsVoucherId != null).ExecuteDeleteAsync();
        await dbContext.Set<ArcaVoucher>().Where(x => x.Cuit == ArcaPersistenceScenario.Cuit).ExecuteDeleteAsync();
        await dbContext.Set<ArcaVoucherSequence>().Where(x => x.Cuit == ArcaPersistenceScenario.Cuit).ExecuteDeleteAsync();
    });
}
