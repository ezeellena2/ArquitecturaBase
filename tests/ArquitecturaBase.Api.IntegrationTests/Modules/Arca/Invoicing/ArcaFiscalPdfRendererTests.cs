using System.Text;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing.Pdf;
using SkiaSharp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>
/// El PDF fiscal sin Docker: un A4 con las fuentes del módulo, los renglones que no entran siguen en otra hoja, y el nombre y el
/// domicilio del receptor se parten en renglones sin perder nada.
/// </summary>
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
        var document = Document(1) with { Lines = [new ArcaFiscalLine(longText, 1m, 1m, 1m)] };

        Assert.Equal(1, Pages(new ArcaFiscalPdfRenderer().Render(document)));
    }

    [Fact]
    public void A_long_receiver_name_and_address_are_wrapped_into_lines_without_losing_anything()
    {
        const string name = "Cooperativa Agrícola Ganadera y de Consumo Limitada del Sur de Santa Fe";
        const string address = "Ruta Nacional 33 km 642, Parque Industrial Venado Tuerto, Lote 14, Venado Tuerto, SANTA FE";
        var document = Document(2) with { ReceiverName = name, ReceiverAddress = address };

        using var regular = ArcaFiscalPdfRenderer.LoadFont("PublicSans-Regular.ttf");
        using var body = new SKFont(regular, ArcaFiscalPdfRenderer.BodySize);
        var lines = ArcaFiscalPdfRenderer.ReceiverLines(document, body);

        Assert.True(lines.Count > 2);
        Assert.DoesNotContain(lines, line => line.Contains('…', StringComparison.Ordinal));
        Assert.Equal($"Apellido y nombre o razón social: {name} Domicilio: {address}", string.Join(' ', lines));
        Assert.Equal(1, Pages(new ArcaFiscalPdfRenderer().Render(document)));
        Assert.Equal(1, Pages(new ArcaFiscalPdfRenderer().Render(document with
        {
            ServiceFrom = new DateOnly(2026, 11, 1), ServiceTo = new DateOnly(2026, 11, 30), PaymentDueOn = new DateOnly(2026, 12, 10),
        })));
    }

    [Fact]
    public void A_class_a_invoice_with_its_rates_subtotals_and_legend_is_one_page()
    {
        var document = Document(3) with
        {
            ClassLetter = "A",
            TypeCode = "COD. 01",
            Lines = [.. Enumerable.Range(1, 3).Select(index => new ArcaFiscalLine($"Ítem {index}", 1m, 100m, 100m, "21%"))],
            Subtotals =
            [
                new("Importe neto gravado", 300m), new("IVA 2,5%", 0m), new("IVA 5%", 0m), new("IVA 10,5%", 0m), new("IVA 21%", 63m),
                new("IVA 27%", 0m), new("Importe exento", 0m), new("Importe no gravado", 0m),
            ],
            Total = 363m,
            Legend = "El crédito fiscal discriminado en el presente comprobante, sólo podrá ser computado a efectos del Régimen de Sostenimiento e "
                + "Inclusión Fiscal para Pequeños Contribuyentes de la Ley Nº 27.618",
        };

        Assert.Equal(1, Pages(new ArcaFiscalPdfRenderer().Render(document)));
    }

    [Fact]
    public void A_class_b_invoice_with_the_transparency_box_still_breaks_long_tables_into_pages()
    {
        var document = Document(80) with { ClassLetter = "B", TypeCode = "COD. 06", VatContained = 1388.43m };

        Assert.True(Pages(new ArcaFiscalPdfRenderer().Render(document)) >= 3);
        Assert.Equal(1, Pages(new ArcaFiscalPdfRenderer().Render(Document(2) with { VatContained = 34.71m })));
    }

    private static int Pages(byte[] pdf) => PageObject().Count(Encoding.Latin1.GetString(pdf));

    [GeneratedRegex(@"/Type\s*/Page\b(?!s)")]
    private static partial Regex PageObject();

    private static ArcaFiscalDocument Document(int lines) => new("Lucía Fernández", "Av. San Martín 1250, Pergamino", "Responsable Monotributo",
        "27-28765433-8", "27-28765433-8", new DateOnly(2019, 3, 1), "C", "COD. 11", "FACTURA", "00004-00000012", new DateOnly(2026, 11, 9),
        "Agro del Sur S.A.", "CUIT 30-71234567-1", "IVA Responsable Inscripto", "Ruta 8 km 225", null, null, null,
        [.. Enumerable.Range(1, lines).Select(index => new ArcaFiscalLine($"Ítem {index}", 1m, 100m, 100m))], 100m * lines, "76451234567890",
        new DateOnly(2026, 11, 19), "https://www.arca.gob.ar/fe/qr/?p=eyJ2ZXIiOjF9", null, [], null, null);
}
