using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// «Documentos recibidos» sorts by every column with a datum (OT1-C, 2026-11-22), by the id of its column in the front and in both
// directions. The data read (one column per question, «datum:…») live in the protected proposal and do not sort, nor does a field of
// no column.
public sealed class ListFormReceivedDocumentsRequestValidatorTests
{
    [Theory]
    [InlineData("receivedAtUtc")]
    [InlineData("document")]
    [InlineData("-document")]
    [InlineData("sender")]
    [InlineData("-sender")]
    [InlineData("form")]
    [InlineData("-form")]
    [InlineData("state")]
    [InlineData("-state")]
    [InlineData("deleteAfterUtc")]
    [InlineData("-deleteAfterUtc")]
    [InlineData("record")]
    [InlineData("-record")]
    public void Every_column_of_the_list_can_be_sorted(string sort) =>
        Assert.True(new ListFormReceivedDocumentsRequestValidator().Validate(new ListFormReceivedDocumentsRequest { Sort = sort }).IsValid);

    [Theory]
    [InlineData("fileName")]
    [InlineData("datum:Monto")]
    public void Other_fields_cannot_be_sorted(string sort) =>
        Assert.False(new ListFormReceivedDocumentsRequestValidator().Validate(new ListFormReceivedDocumentsRequest { Sort = sort }).IsValid);
}
