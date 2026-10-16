using SkiaSharp;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

internal static class FormPdfFonts
{
    public static SKTypeface Load(bool bold)
    {
        var name = bold ? "PublicSans-Bold.ttf" : "PublicSans-Regular.ttf";
        using var stream = typeof(FormPdfFonts).Assembly.GetManifestResourceStream("BusinessForms.Pdf.Fonts." + name)
            ?? throw new InvalidOperationException("An embedded PDF font is missing.");
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException("An embedded PDF font is invalid.");
    }
}
