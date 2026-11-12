using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Task 7, decision 16: a received document is searched by numbers kept as digits beside its protected reading: with a form, only the typed
// values its proposal marked searchable; without one, only the amounts and the CUIT, CBU or CVU whose check digits pass. Never a card, and
// never digit groups joined across spaces.
public sealed class FormReceivedDocumentSearchPolicyTests
{
    private const string Cuit = "20-12345678-6";
    private const string Cbu = "0170099240000012345672";

    [Fact]
    public void A_document_with_a_form_keeps_only_its_searchable_typed_values_and_never_its_reading()
    {
        var proposal = new FormDocumentPreparedProposal("sure", "comprobante", [new(Guid.NewGuid(), Guid.NewGuid(), 1,
        [
            new(Guid.NewGuid(), "decimal", "150000.5", null) { Searchable = true },
            new(Guid.NewGuid(), "shortText", Cuit, null) { Searchable = true },
            new(Guid.NewGuid(), "shortText", "0001 2345", null) { Searchable = true },
            new(Guid.NewGuid(), "shortText", "4111111111111111", null) { Searchable = true },
            new(Guid.NewGuid(), "shortText", "351 555 0101", null),
            new(Guid.NewGuid(), "shortText", "Juan", null),
            new(Guid.NewGuid(), "choice", Guid.NewGuid().ToString("D"), null),
            new(Guid.NewGuid(), "decimal", null, FormDocumentMarks.NotRead),
        ])]);
        var reading = Reading($"| Monto | $ 150.000,50 |\n| CBU | {Cbu} |\n| Operación | 778899 |");

        // The operation is kept by its groups, never joined; the card number and the phone, which is not typed, are not kept.
        Assert.Equal("1500005 150000 20123456786 0001 2345", FormReceivedDocumentSearchPolicy.Build(reading, proposal));
        Assert.Equal("", FormReceivedDocumentSearchPolicy.Build(null, null));
    }

    [Fact]
    public void A_document_without_a_form_keeps_only_amounts_and_the_CUIT_CBU_or_CVU_that_pass_their_check_digits()
    {
        var reading = Reading(string.Join('\n', "Tel: 351 555-0101", "Celular 3515550101", "Tarjeta 4111 1111 1111 1111", "Tarjeta 4111111111111111",
            "Monto $ 150.000,50", "Total 2.500,00", $"CUIT {Cuit}", "CUIT 20-12345678-0", $"CBU {Cbu}", "CBU 0170099240000012345670", "Hora 14:35",
            "Factura 0001-00012345"));

        Assert.Equal("150000 2500 20123456786 " + Cbu, FormReceivedDocumentSearchPolicy.Build(reading, null));
        // A proposal that names no form is still a document without one.
        Assert.Equal("150000 2500 20123456786 " + Cbu, FormReceivedDocumentSearchPolicy.Build(reading, new FormDocumentPreparedProposal("unknown", null, [])));
    }

    [Fact]
    public void The_kept_numbers_never_pass_their_column_and_a_search_is_only_its_digits_with_enough_of_them()
    {
        var reading = Reading(string.Join(" | ", Enumerable.Range(1000, 2000).Select(number => "$ " + number.ToString(CultureInfo.InvariantCulture))));
        Assert.InRange(FormReceivedDocumentSearchPolicy.Build(reading, null).Length, 1, FormReceivedDocument.MaxSearchTextLength);
        Assert.Equal("150000", FormReceivedDocumentSearchPolicy.Search("$ 150.000"));
        Assert.Equal("20123456786", FormReceivedDocumentSearchPolicy.Search(Cuit));
        Assert.Null(FormReceivedDocumentSearchPolicy.Search("12"));
        Assert.Null(FormReceivedDocumentSearchPolicy.Search("Galicia"));
        Assert.Null(FormReceivedDocumentSearchPolicy.Search(null));
    }

    private static StructuredDocumentReading Reading(string text) => new(StructuredDocumentReadability.Readable, null, "es", text, false);
}
