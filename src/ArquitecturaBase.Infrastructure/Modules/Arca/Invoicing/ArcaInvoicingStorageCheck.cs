using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using Microsoft.Extensions.Hosting;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;

/// <summary>
/// Con la facturación instalada, la Api no arranca sin el almacén privado (H7): ahí va el PDF fiscal de cada comprobante, y sin
/// él una factura autorizada quedaría para siempre sin su documento. Es una falla del despliegue y conviene verla al publicar.
/// </summary>
internal sealed class ArcaInvoicingStorageCheck(IPrivateFileStore files) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!files.IsAvailable)
            throw new InvalidOperationException("ARCA invoicing needs the private file store for the fiscal PDFs; configure FileStorage (see docs/guides/configurar-arca.md).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
