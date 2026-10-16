using System.Globalization;
using SkiaSharp;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

internal static class FormPdfTextLayout
{
    public static IReadOnlyList<string> Wrap(string text, SKFont font, float width)
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
                    if (font.MeasureText(paragraph[elements[start]..end]) <= width)
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
