using System.Text;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// The reading of documents (Parte L): the objects of a PDF are counted on its bytes before PdfPig opens it, as an upper bound that
// formatting cannot hide. Technical tests, without a database.
public sealed class FormPdfObjectsTests
{
    [Theory]
    [InlineData("1 0 obj<<>>endobj\n2 0 obj[]endobj", 2)]
    [InlineData("1 %comentario\n0 %otro\nobj<<>>endobj", 1)]
    [InlineData("1 0obj<<>>endobj", 1)]
    [InlineData("% 1 0 obj dentro de un comentario\n", 1)]
    [InlineData("endobj Xobj /Xobj (objeto)", 0)]
    public void Every_object_header_counts_whatever_comes_before_its_keyword(string pdf, long expected) =>
        Assert.Equal(expected, FormPdfObjects.Count(Encoding.ASCII.GetBytes(pdf), long.MaxValue));

    // An object stream declares its objects with /N, with or without its /Type, also as a real or after a comment, and PdfPig reads
    // the name with its #xx escapes. A reference (the normal appearance of an annotation) and a negative number add nothing.
    [Theory]
    [InlineData("<< /Type /ObjStm /N 150 /First 900 >>", 150)]
    [InlineData("<< /N 150 /First 900 >>", 150)]
    [InlineData("<< /#4E 150 /#4e 50 >>", 200)]
    [InlineData("<< /N %oculto\n 150 >>", 150)]
    [InlineData("<< /N 150.7 >>", 150)]
    [InlineData("<< /N +150 >>", 150)]
    [InlineData("<< /AP << /N 12345 0 R >> >>", 0)]
    [InlineData("<< /AP << /N 12345 %c\n0 R >> >>", 0)]
    [InlineData("<< /N -5 /Names 900 /NN 900 /N#20 900 >>", 0)]
    [InlineData("[/N 3 0 /X]", 3)]
    public void An_object_stream_adds_the_objects_its_dictionary_declares(string pdf, long expected) =>
        Assert.Equal(expected, FormPdfObjects.Count(Encoding.ASCII.GetBytes(pdf), long.MaxValue));

    [Fact]
    public void Counting_stops_just_past_the_limit()
    {
        var pdf = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(1, 100).Select(number => $"{number} 0 obj<<>>endobj\n")));

        Assert.Equal(4, FormPdfObjects.Count(pdf, 3));
        Assert.Equal(100, FormPdfObjects.Count(pdf, 1_000));
    }
}
