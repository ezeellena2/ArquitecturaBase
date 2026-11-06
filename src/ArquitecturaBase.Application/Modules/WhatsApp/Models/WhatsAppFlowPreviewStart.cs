using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// Where Meta's official preview starts when it runs interactively (VO1-A, 2026-11-06): the first screen of the published Flow and
/// the sample options of its catalog lists, the same data a message that opens the form sends.
/// </summary>
public sealed record WhatsAppFlowPreviewStart(string Screen, IReadOnlyList<StructuredFormCatalogOffer> Catalogs)
{
    public override string ToString() => "WhatsAppFlowPreviewStart { Content = [REDACTED] }";
}
