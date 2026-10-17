using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

internal sealed class FormPdfTextLayout : IDisposable
{
    private readonly SKFont primary;
    private readonly SKFont emoji;
    private readonly SKShaper shaper;

    public FormPdfTextLayout(SKTypeface face, SKTypeface emojiFace, float size)
    {
        primary = new(face, size);
        emoji = new(emojiFace, size);
        shaper = new(emojiFace);
    }

    public float MeasureText(string text) => Runs(text).Sum(run =>
        run.UsesEmoji ? shaper.Shape(run.Text, emoji).Width : primary.MeasureText(run.Text));

    public void DrawText(SKCanvas canvas, string text, float x, float y, SKPaint paint)
    {
        foreach (var run in Runs(text))
        {
            if (run.UsesEmoji)
            {
                canvas.DrawShapedText(shaper, run.Text, x, y, SKTextAlign.Left, emoji, paint);
                x += shaper.Shape(run.Text, emoji).Width;
            }
            else
            {
                canvas.DrawText(run.Text, x, y, SKTextAlign.Left, primary, paint);
                x += primary.MeasureText(run.Text);
            }
        }
    }

    private IEnumerable<TextRun> Runs(string text)
    {
        if (text.Length == 0) yield break;
        if (primary.ContainsGlyphs(text))
        {
            yield return new(text, false);
            yield break;
        }
        var elements = StringInfo.GetTextElementEnumerator(text);
        var buffer = new StringBuilder();
        var fallback = false;
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var next = !primary.ContainsGlyphs(element);
            if (buffer.Length > 0 && next != fallback)
            {
                yield return new(buffer.ToString(), fallback);
                buffer.Clear();
            }
            buffer.Append(element);
            fallback = next;
        }
        if (buffer.Length > 0) yield return new(buffer.ToString(), fallback);
    }

    private sealed record TextRun(string Text, bool UsesEmoji);

    public void Dispose()
    {
        primary.Dispose();
        emoji.Dispose();
        shaper.Dispose();
    }

    public IReadOnlyList<string> Wrap(string text, float width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add("");
                continue;
            }
            var elements = StringInfo.ParseCombiningCharacters(paragraph);
            var start = 0;
            while (start < elements.Length)
            {
                var low = 1;
                var high = elements.Length - start;
                var count = 1;
                while (low <= high)
                {
                    var candidate = (low + high) / 2;
                    var end = start + candidate == elements.Length ? paragraph.Length : elements[start + candidate];
                    if (MeasureText(paragraph[elements[start]..end]) <= width)
                    {
                        count = candidate;
                        low = candidate + 1;
                    }
                    else high = candidate - 1;
                }
                var endIndex = start + count == elements.Length ? paragraph.Length : elements[start + count];
                if (start + count < elements.Length)
                {
                    var space = paragraph.LastIndexOf(' ', endIndex - 1, endIndex - elements[start]);
                    if (space >= elements[start])
                    {
                        var boundary = Array.BinarySearch(elements, space + 1);
                        if (boundary > start)
                        {
                            count = boundary - start;
                            endIndex = space + 1;
                        }
                    }
                }
                lines.Add(paragraph[elements[start]..endIndex]);
                start += count;
            }
        }
        return lines;
    }
}
