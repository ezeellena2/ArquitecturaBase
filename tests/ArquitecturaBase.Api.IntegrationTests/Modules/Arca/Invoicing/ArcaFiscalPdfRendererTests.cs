using System.Text;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing.Pdf;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>El PDF fiscal sin Docker: un A4 con las fuentes del módulo, y los renglones que no entran siguen en otra hoja.</summary>
public sealed partial class ArcaFiscalPdfRendererTests
{
    [Fact]
    public void A_short_invoice_is_one_page()
    {
        var bytes = new ArcaFiscalPdfRenderer().Render(Document(2));

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(1, Pages(bytes));
    }

    [Fact]
    public void Lines_that_do_not_fit_go_on_to_the_next_page()
    {
        Assert.True(Pages(new ArcaFiscalPdfRenderer().Render(Document(80))) >= 3);
    }

    [Fact]
    public void A_long_description_is_wrapped_and_not_lost()
    {
        var longText = string.Join(' ', Enumerable.Repeat("Semilla de soja certificada", 7));
        var document = Document(1) with { Lines = [new ArcaVoucherLine(longText, 1m, 1m, 1m)] };

        Assert.Equal(1, Pages(new ArcaFiscalPdfRenderer().Render(document)));
    }

    private static int Pages(byte[] pdf) => PageObject().Count(Encoding.Latin1.GetString(pdf));

    [GeneratedRegex(@"/Type\s*/Page\b(?!s)")]
    private static partial Regex PageObject();

    private static ArcaFiscalDocument Document(int lines) => new("Lucía Fernández", "Av. San Martín 1250, Pergamino", "Responsable Monotributo",
        "27-28765433-8", "27-28765433-8", new DateOnly(2019, 3, 1), "C", "COD. 11", "FACTURA", "00004-00000012", new DateOnly(2026, 11, 9),
        "Agro del Sur S.A.", "CUIT 30-71234567-1", "IVA Responsable Inscripto", "Ruta 8 km 225", null, null, null,
        [.. Enumerable.Range(1, lines).Select(index => new ArcaVoucherLine($"Ítem {index}", 1m, 100m, 100m))], 100m * lines, "76451234567890",
        new DateOnly(2026, 11, 19), "https://www.arca.gob.ar/fe/qr/?p=eyJ2ZXIiOjF9", null);
}
