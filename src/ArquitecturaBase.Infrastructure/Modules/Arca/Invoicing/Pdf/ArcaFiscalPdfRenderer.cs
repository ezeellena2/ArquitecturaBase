using System.Globalization;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using QRCoder;
using SkiaSharp;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing.Pdf;

/// <summary>
/// El PDF fiscal con su diseño fijo, en A4: «ORIGINAL», el emisor y el comprobante con la letra y el código en el recuadro del
/// medio, el receptor (el nombre y el domicilio enteros, partidos en los renglones que hagan falta), el período si es un
/// servicio, los renglones (en varias páginas si hace falta, con el encabezado de la
/// tabla repetido), el total, el CAE con su vencimiento y el QR de ARCA. En la A, la tabla suma la alícuota y, arriba del pie,
/// van a la derecha el neto y el IVA de cada alícuota; abajo a la izquierda, la leyenda de la RG 5003 o, en la B, el recuadro
/// de la RG 5614. Usa las fuentes incrustadas del módulo y QRCoder sólo para la matriz del QR, que se dibuja con Skia.
/// </summary>
internal sealed class ArcaFiscalPdfRenderer : IArcaFiscalPdfRenderer
{
    private const float Width = 595.276f;
    private const float Height = 841.89f;
    private const float Margin = 36;
    private const float Right = Width - Margin;
    private const float RowHeight = 16;
    private const float FooterHeight = 150;
    private const float SubtotalHeight = 13;
    private const float LegendWidth = 260;
    private const float SubtotalLabel = 215;
    private const float ReceiverRow = 14;
    private const float ReceiverWidth = Right - Width / 2 - 12;
    internal const float BodySize = 9;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-AR");

    public byte[] Render(ArcaFiscalDocument document)
    {
        using var regular = LoadFont("PublicSans-Regular.ttf");
        using var bold = LoadFont("PublicSans-Bold.ttf");
        using var output = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata { Title = $"{document.Title} {document.Number}", Creator = "ARCA" }))
        {
            using var layout = new Layout(pdf, regular, bold);
            layout.Draw(document);
            pdf.Close();
        }

        return output.ToArray();
    }

    /// <summary>
    /// Los renglones de la columna derecha del recuadro del receptor, con la fuente del cuerpo: el nombre y el domicilio partidos
    /// por palabras para que entren en su ancho, sin recortar nada. Es un dato fiscal y se imprime entero.
    /// </summary>
    internal static List<string> ReceiverLines(ArcaFiscalDocument document, SKFont font)
    {
        var lines = Wrap($"Apellido y nombre o razón social: {document.ReceiverName}", font, ReceiverWidth);
        if (document.ReceiverAddress is { } address) lines.AddRange(Wrap($"Domicilio: {address}", font, ReceiverWidth));
        return lines;
    }

    /// <summary>El texto partido por palabras en renglones que entran en el ancho; una palabra más larga se corta.</summary>
    private static List<string> Wrap(string text, SKFont font, float width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) <= width)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0) lines.Add(current);
            current = word;
            while (font.MeasureText(current) > width && current.Length > 1)
            {
                var cut = current.Length - 1;
                while (cut > 1 && font.MeasureText(current[..cut]) > width) cut--;
                lines.Add(current[..cut]);
                current = current[cut..];
            }
        }

        if (current.Length > 0 || lines.Count == 0) lines.Add(current);
        return lines;
    }

    internal static SKTypeface LoadFont(string name)
    {
        using var stream = typeof(ArcaFiscalPdfRenderer).Assembly.GetManifestResourceStream("Arca.Pdf.Fonts." + name)
            ?? throw new InvalidOperationException("An embedded fiscal PDF font is missing.");
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException("An embedded fiscal PDF font is invalid.");
    }

    private static string Money(decimal value) => value.ToString("#,##0.00", Spanish);

    private static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private sealed class Layout : IDisposable
    {
        private readonly SKDocument pdf;
        private readonly SKFont body;
        private readonly SKFont small;
        private readonly SKFont smallStrong;
        private readonly SKFont strong;
        private readonly SKFont title;
        private readonly SKFont letter;
        private readonly SKPaint ink = new() { Color = SKColors.Black, IsAntialias = true };
        private readonly SKPaint stroke = new() { Color = SKColors.Black, IsAntialias = true, IsStroke = true, StrokeWidth = 0.8f };
        private readonly SKPaint rule = new() { Color = SKColor.Parse("#BBBBBB"), IsAntialias = true, StrokeWidth = 0.5f };
        private SKCanvas canvas = null!;
        private int page;
        private bool withVat;

        public Layout(SKDocument pdf, SKTypeface regular, SKTypeface bold)
        {
            this.pdf = pdf;
            body = new SKFont(regular, BodySize);
            small = new SKFont(regular, 7.5f);
            smallStrong = new SKFont(bold, 7.5f);
            strong = new SKFont(bold, 9);
            title = new SKFont(bold, 13);
            letter = new SKFont(bold, 30);
        }

        public void Draw(ArcaFiscalDocument document)
        {
            withVat = document.Lines.Any(line => line.Vat is not null);
            var legend = document.Legend is { } text ? Wrap(text, small, LegendWidth) : [];
            var reserve = Reserve(document, legend);
            var y = StartPage(document);
            y = Receiver(document, y);
            y = TableHeader(y);
            foreach (var line in document.Lines)
            {
                var description = Wrap(line.Description, body, withVat ? 240 : 280);
                var height = RowHeight + (description.Count - 1) * 11;
                if (y + height > Height - Margin - FooterHeight - reserve)
                {
                    pdf.EndPage();
                    y = TableHeader(StartPage(document));
                }

                for (var index = 0; index < description.Count; index++) Text(body, description[index], Margin + 6, y + 11 + index * 11);
                Number(body, line.Quantity.ToString("0.####", Spanish), Margin + (withVat ? 300 : 340), y + 11);
                Number(body, Money(line.UnitPrice), Margin + (withVat ? 385 : 430), y + 11);
                if (line.Vat is { } vat) Number(body, vat, Margin + 450, y + 11);
                Number(body, Money(line.Amount), Right - 6, y + 11);
                canvas.DrawLine(Margin, y + height, Right, y + height, rule);
                y += height;
            }

            Above(document, legend, reserve);
            Footer(document);
            pdf.EndPage();
        }

        public void Dispose()
        {
            body.Dispose();
            small.Dispose();
            smallStrong.Dispose();
            strong.Dispose();
            title.Dispose();
            letter.Dispose();
            ink.Dispose();
            stroke.Dispose();
            rule.Dispose();
        }

        /// <summary>El encabezado de cada página: «ORIGINAL», el emisor, la letra con su código y el comprobante.</summary>
        private float StartPage(ArcaFiscalDocument document)
        {
            canvas = pdf.BeginPage(Width, Height);
            page++;
            canvas.DrawRect(Margin, Margin, Right - Margin, 18, stroke);
            Center(strong, "ORIGINAL", Width / 2, Margin + 12.5f);
            var top = Margin + 18;
            canvas.DrawRect(Margin, top, Right - Margin, 104, stroke);
            var middle = Width / 2;
            canvas.DrawRect(middle - 26, top, 52, 50, stroke);
            Center(letter, document.ClassLetter, middle, top + 32);
            Center(small, document.TypeCode, middle, top + 44);
            canvas.DrawLine(middle, top + 50, middle, top + 104, stroke);

            Text(title, Fit(document.IssuerName, title, middle - Margin - 40), Margin + 8, top + 20);
            Text(body, Fit(document.IssuerAddress, body, middle - Margin - 40), Margin + 8, top + 36);
            Text(body, document.IssuerTaxStatus, Margin + 8, top + 50);

            var x = middle + 34;
            Text(title, document.Title, x, top + 20);
            Text(body, $"Punto de venta: {document.Number[..5]}   Comp. nro.: {document.Number[6..]}", x, top + 36);
            Text(body, $"Fecha de emisión: {Date(document.IssuedOn)}", x, top + 50);
            Text(body, $"CUIT: {document.IssuerCuit}", x, top + 66);
            if (document.GrossIncomeNumber is { } grossIncome) Text(body, $"Ingresos brutos: {grossIncome}", x, top + 80);
            if (document.ActivityStartOn is { } start) Text(body, $"Inicio de actividades: {Date(start)}", x, top + 94);
            if (page > 1) Text(small, $"Hoja {page}", Margin + 8, top + 96);
            return top + 104;
        }

        /// <summary>
        /// El recuadro del receptor: a la izquierda el documento, la condición y lo que anula; a la derecha el nombre y el domicilio
        /// en los renglones que hagan falta. El recuadro crece con ellos y el período va debajo de todo, a lo ancho.
        /// </summary>
        private float Receiver(ArcaFiscalDocument document, float y)
        {
            var lines = ReceiverLines(document, body);
            var rows = Math.Max(3, lines.Count);
            var height = ReceiverRow * rows + 6 + (document.ServiceFrom is null ? 0 : ReceiverRow);
            canvas.DrawRect(Margin, y, Right - Margin, height, stroke);
            var middle = Width / 2;
            Text(body, document.ReceiverDocument, Margin + 8, y + ReceiverRow);
            Text(body, $"Condición frente al IVA: {document.ReceiverVatCondition}", Margin + 8, y + 2 * ReceiverRow);
            for (var index = 0; index < lines.Count; index++) Text(body, lines[index], middle + 4, y + (index + 1) * ReceiverRow);
            if (document.Cancels is { } cancels) Text(body, cancels, Margin + 8, y + 3 * ReceiverRow);
            if (document.ServiceFrom is { } from && document.ServiceTo is { } to)
            {
                var due = document.PaymentDueOn is { } payment ? $"   Vencimiento del pago: {Date(payment)}" : string.Empty;
                Text(body, $"Período facturado desde: {Date(from)}   hasta: {Date(to)}{due}", Margin + 8, y + (rows + 1) * ReceiverRow);
            }

            return y + height + 8;
        }

        private float TableHeader(float y)
        {
            canvas.DrawRect(Margin, y, Right - Margin, RowHeight, stroke);
            Text(strong, "Descripción", Margin + 6, y + 11);
            Number(strong, "Cantidad", Margin + (withVat ? 300 : 340), y + 11);
            Number(strong, "Precio unit.", Margin + (withVat ? 385 : 430), y + 11);
            if (withVat) Number(strong, "Alícuota", Margin + 450, y + 11);
            Number(strong, "Subtotal", Right - 6, y + 11);
            return y + RowHeight;
        }

        /// <summary>
        /// Lo que va arriba del pie se reserva en cada hoja, para que la tabla no lo pise: los subtotales de la A a la derecha, y
        /// el recuadro de la RG 5614 o la leyenda de la RG 5003 a la izquierda.
        /// </summary>
        private static float Reserve(ArcaFiscalDocument document, List<string> legend)
        {
            var right = document.Subtotals.Count == 0 ? 0 : document.Subtotals.Count * SubtotalHeight + 6;
            var left = document.VatContained is not null ? 46 : legend.Count == 0 ? 0 : legend.Count * 10 + 6;
            var height = Math.Max(right, left);
            return height == 0 ? 0 : height + 8;
        }

        private void Above(ArcaFiscalDocument document, List<string> legend, float reserve)
        {
            if (reserve == 0) return;
            var top = Height - Margin - FooterHeight + 20 - reserve;
            for (var index = 0; index < document.Subtotals.Count; index++)
            {
                var subtotal = document.Subtotals[index];
                Text(body, subtotal.Label, Right - SubtotalLabel, top + 11 + index * SubtotalHeight);
                Number(body, $"$ {Money(subtotal.Amount)}", Right - 10, top + 11 + index * SubtotalHeight);
            }

            if (document.VatContained is { } vat)
            {
                canvas.DrawRect(Margin, top, 270, 46, stroke);
                Text(smallStrong, "Régimen de Transparencia Fiscal al Consumidor (Ley 27.743)", Margin + 6, top + 12);
                Text(body, $"IVA Contenido: $ {Money(vat)}", Margin + 6, top + 26);
                Text(body, $"Otros Impuestos Nacionales Indirectos: $ {Money(0m)}", Margin + 6, top + 39);
            }

            for (var index = 0; index < legend.Count; index++) Text(small, legend[index], Margin, top + 10 + index * 10);
        }

        /// <summary>El pie de la última página: el total, el CAE con su vencimiento y el QR.</summary>
        private void Footer(ArcaFiscalDocument document)
        {
            var top = Height - Margin - FooterHeight + 20;
            canvas.DrawRect(Margin, top, Right - Margin, FooterHeight - 20, stroke);
            Number(title, $"Importe total: $ {Money(document.Total)}", Right - 10, top + 22);
            DrawQr(document.QrUrl, Margin + 10, top + 12, 96);
            Text(strong, "Comprobante autorizado", Margin + 120, top + 50);
            Text(body, $"CAE: {document.Cae}", Margin + 120, top + 66);
            Text(body, $"Vencimiento del CAE: {Date(document.CaeDueOn)}", Margin + 120, top + 80);
        }

        private void DrawQr(string text, float x, float y, float size)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            var modules = data.ModuleMatrix;
            var cell = size / modules.Count;
            for (var row = 0; row < modules.Count; row++)
            {
                for (var column = 0; column < modules[row].Length; column++)
                {
                    if (modules[row][column]) canvas.DrawRect(x + column * cell, y + row * cell, cell, cell, ink);
                }
            }
        }

        private void Text(SKFont font, string text, float x, float baseline) => canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, ink);

        private void Center(SKFont font, string text, float x, float baseline) => canvas.DrawText(text, x, baseline, SKTextAlign.Center, font, ink);

        private void Number(SKFont font, string text, float right, float baseline) => canvas.DrawText(text, right, baseline, SKTextAlign.Right, font, ink);

        private static string Fit(string text, SKFont font, float width)
        {
            if (font.MeasureText(text) <= width) return text;
            var cut = text;
            while (cut.Length > 1 && font.MeasureText(cut + "…") > width) cut = cut[..^1];
            return cut.TrimEnd() + "…";
        }
    }
}
