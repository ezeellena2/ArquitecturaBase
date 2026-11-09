using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// RE1-A: what an external action returned is kept in the response, one row per run, sealed with its names.
public sealed class BusinessFormRecordResultTests
{
    private static readonly DateTime At = new(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_result_keeps_its_run_source_and_sealed_content()
    {
        var (definition, record, run) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        var result = BusinessFormRecordResult.Create(definition, record, run, "arca.invoice", """{"es":"Emitir comprobante en ARCA"}""", "[]", At);

        Assert.Equal((definition, record, run, "arca.invoice", At), (result.DefinitionId, result.RecordId, result.RunId, result.Source, result.RecordedAtUtc));
        Assert.Equal("[]", result.ValuesJson);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public void Its_identities_source_content_and_time_are_required()
    {
        var id = Guid.CreateVersion7();
        Assert.ThrowsAny<ArgumentException>(() => BusinessFormRecordResult.Create(Guid.Empty, id, id, "arca.invoice", "{}", "[]", At));
        Assert.ThrowsAny<ArgumentException>(() => BusinessFormRecordResult.Create(id, id, Guid.Empty, "arca.invoice", "{}", "[]", At));
        Assert.ThrowsAny<ArgumentException>(() => BusinessFormRecordResult.Create(id, id, id, "ARCA invoice", "{}", "[]", At));
        Assert.ThrowsAny<ArgumentException>(() => BusinessFormRecordResult.Create(id, id, id, new string('a', 65), "{}", "[]", At));
        Assert.ThrowsAny<ArgumentException>(() => BusinessFormRecordResult.Create(id, id, id, "arca.invoice", " ", "[]", At));
        Assert.ThrowsAny<ArgumentException>(() => BusinessFormRecordResult.Create(id, id, id, "arca.invoice", "{}", "[]", DateTime.SpecifyKind(At, DateTimeKind.Local)));
    }
}
