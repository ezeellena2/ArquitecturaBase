using SkiaSharp;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

internal static class FormPdfFonts
{
    public static SKTypeface Load(bool bold) => Load(bold ? "PublicSans-Bold.ttf" : "PublicSans-Regular.ttf");

    public static SKTypeface LoadEmoji() => Load("NotoEmoji.ttf");

    private static SKTypeface Load(string name)
    {
        using var stream = typeof(FormPdfFonts).Assembly.GetManifestResourceStream("BusinessForms.Pdf.Fonts." + name)
            ?? throw new InvalidOperationException("An embedded PDF font is missing.");
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException("An embedded PDF font is invalid.");
    }
}
