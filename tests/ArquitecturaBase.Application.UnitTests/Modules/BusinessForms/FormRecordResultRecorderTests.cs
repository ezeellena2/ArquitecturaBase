using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// RE1-A: the result of an external action, kept in the response with its names as they were. A malformed result is a bug of the
// module that produced it, so it throws instead of being saved half.
public sealed class FormRecordResultRecorderTests
{
    private sealed class Results : IFormRecordResultRepository
    {
        public List<BusinessFormRecordResult> Added { get; } = [];
        public void Add(BusinessFormRecordResult result) => Added.Add(result);
    }

    private static readonly IReadOnlyDictionary<string,string> Name = new Dictionary<string,string> { ["es"]="Emitir comprobante en ARCA", ["en"]="Issue voucher in ARCA" };

    private static StructuredRecordResultValue Value(string key, string kind, string? value) =>
        new(key, new Dictionary<string,string> { ["es"]=key }, kind, value);

    private static StructuredRecordResult Invoice(params StructuredRecordResultValue[] values) =>
        new("arca.invoice", Name, values.Length == 0
            ? [Value("voucher","text","Factura C 0004-00000012"), Value("authorization","text","74512345678901"), Value("expiresOn","date","2026-11-19"), Value("total","number","15300.5")]
            : values);

    private static (FormRecordResultRecorder Recorder, Results Results, FakeTimeProvider Time) Create()
    {
        var results = new Results();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero));
        return (new FormRecordResultRecorder(results, time), results, time);
    }

    [Fact]
    public void A_result_is_added_with_its_source_names_and_values_as_they_came()
    {
        var (recorder, results, time) = Create();
        var (definition, record, run) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        recorder.Add(definition, record, run, Invoice());

        var added = Assert.Single(results.Added);
        Assert.Equal((definition, record, run, "arca.invoice", time.GetUtcNow().UtcDateTime),
            (added.DefinitionId, added.RecordId, added.RunId, added.Source, added.RecordedAtUtc));
        var stored = FormRecordResultRecorder.Read(added);
        Assert.Equal("Emitir comprobante en ARCA", stored.SourceName["es"]);
        Assert.Equal(["voucher","authorization","expiresOn","total"], stored.Values.Select(value => value.Key));
        Assert.Equal("15300.5", stored.Values[3].Value);
    }

    [Theory]
    [InlineData("expiresOn","date","19/11/2026")]
    [InlineData("total","number","15.300,50")]
    [InlineData("total","number","1e3")]
    [InlineData("total","money","1")]
    [InlineData("Total con espacios","text","x")]
    public void A_value_out_of_its_kind_or_key_throws(string key, string kind, string value)
    {
        var (recorder, results, _) = Create();
        Assert.ThrowsAny<ArgumentException>(() => recorder.Add(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Invoice(Value(key, kind, value))));
        Assert.Empty(results.Added);
    }

    [Fact]
    public void Keys_are_unique_and_a_value_may_be_empty()
    {
        var (recorder, results, _) = Create();
        Assert.ThrowsAny<ArgumentException>(() => recorder.Add(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Invoice(Value("voucher","text","a"), Value("voucher","text","b"))));
        recorder.Add(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Invoice(Value("voucher","text",null)));
        Assert.Single(results.Added);
    }
}
