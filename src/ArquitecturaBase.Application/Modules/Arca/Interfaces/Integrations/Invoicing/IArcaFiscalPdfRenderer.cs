using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;

/// <summary>Dibuja el PDF fiscal con su diseño fijo y el QR de ARCA. No guarda nada.</summary>
public interface IArcaFiscalPdfRenderer
{
    byte[] Render(ArcaFiscalDocument document);
}
