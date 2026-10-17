using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormPdfEmojiTests
{
    [Theory]
    [InlineData("😀")]
    [InlineData("👍🏽")]
    [InlineData("🚧")]
    [InlineData("📸")]
    [InlineData("👩‍💻")]
    [InlineData("🇦🇷")]
    public void An_emoji_grapheme_is_drawn_as_its_real_shaped_outline_instead_of_the_primary_missing_glyph(string text)
    {
        using var primary = FormPdfFonts.Load(false);
        using var emojiFace = FormPdfFonts.LoadEmoji();
        using var font = new SKFont(emojiFace, 18);
        using var shaper = new SKShaper(emojiFace);
        var shaped = shaper.Shape(text, font);
        Assert.NotEqual(0u, Assert.Single(shaped.Codepoints));
        using var expected = new SKBitmap(96, 48);
        using var actual = new SKBitmap(96, 48);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using (var canvas = new SKCanvas(expected))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawShapedText(shaper, text, 8, 32, SKTextAlign.Left, font, paint);
        }
        using var layout = new FormPdfTextLayout(primary, emojiFace, 18);
        using (var canvas = new SKCanvas(actual))
        {
            canvas.Clear(SKColors.White);
            layout.DrawText(canvas, text, 8, 32, paint);
        }
        Assert.Contains(expected.Pixels, pixel => pixel != SKColors.White);
        Assert.Equal(expected.Pixels, actual.Pixels);
        Assert.InRange(Math.Abs(layout.MeasureText(text) - shaped.Width), 0, 0.01f);
    }

    [Fact]
    public void Mixed_text_wraps_with_the_same_fonts_used_for_drawing_without_losing_graphemes()
    {
        using var primary = FormPdfFonts.Load(false);
        using var emojiFace = FormPdfFonts.LoadEmoji();
        using var layout = new FormPdfTextLayout(primary, emojiFace, 10);
        var text = "Revisá áéíóú ñ " + new string('W', 300) + " e\u0301 😀👍🏽🚧📸👩‍💻🇦🇷 final";
        var lines = layout.Wrap(text, 110);
        Assert.True(lines.Count > 3);
        Assert.Equal(text, string.Concat(lines));
        Assert.All(lines, line => Assert.InRange(layout.MeasureText(line), 0, 110.01f));
        Assert.All(lines, line => Assert.False(line.Length > 0 && char.IsLowSurrogate(line[0])));
        Assert.All(lines, line => Assert.False(line.EndsWith('\u200D')));
        Assert.Equal(new[] { "Primera", "", "Última" }, layout.Wrap("Primera\r\n\r\nÚltima", 200));
    }
}
