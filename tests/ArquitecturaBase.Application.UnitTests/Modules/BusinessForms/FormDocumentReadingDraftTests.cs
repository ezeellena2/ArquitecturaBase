using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// DOC1: the reading of documents is a setting of the draft, like «Dónde se completa». «Cómo reconocerlo» goes trimmed, up to 1000
// characters and required while it is on; up to five lookups fill a catalog question with what another question of the draft read.
// Without a reading nothing is written, so drafts and fingerprints keep their bytes; turned off, it keeps what was written.
public sealed class FormDocumentReadingDraftTests
{
    private static readonly IReadOnlyDictionary<string, string> Text = new Dictionary<string, string> { ["es"] = "Pago", ["en"] = "Payment" };
    private static readonly Guid Catalog = Guid.CreateVersion7();
    private static readonly Guid CatalogField = Guid.CreateVersion7();
    private static readonly string Customer = Guid.CreateVersion7().ToString("D"), TaxId = Guid.CreateVersion7().ToString("D"),
        Amount = Guid.CreateVersion7().ToString("D"), Notes = Guid.CreateVersion7().ToString("D");

    private static FormFieldInput[] Fields() =>
    [
        new(Customer, null, null, "choice", Text, null, false, null, null, new FormQuestionSource(Catalog)),
        new(TaxId, null, null, "shortText", Text, null, false, 13, null),
        new(Amount, null, null, "decimal", Text, null, true, null, null),
        new(Notes, null, null, "longText", Text, null, false, 600, null),
    ];

    private static FormDocumentLookup Lookup(string field = FormDocumentLookup.Code, string? question = null, string? source = null) =>
        new(Guid.Parse(question ?? Customer), Guid.Parse(source ?? TaxId), field);

    private static SaveFormDraftRequest Request(FormDocumentReading? reading, FormFieldInput[]? fields = null) =>
        new(Guid.NewGuid(), Text, "PAG", new(1, "es", Text, fields ?? Fields(), DocumentReading: reading));

    private static IEnumerable<string> Errors(SaveFormDraftRequest request) =>
        new SaveFormDraftRequestValidator().Validate(request).Errors.Select(error => error.PropertyName);

    [Fact]
    public void Two_lookups_never_fill_the_same_question() =>
        Assert.Contains("draft.documentReading.lookups[1].questionId",
            Errors(Request(new(true, "Pago", [Lookup(), Lookup(FormDocumentLookup.Text, source: Notes)]))));

    [Fact]
    public void A_reading_with_its_lookups_is_saved_trimmed_and_names_new_questions_by_their_field_ids()
    {
        var valid = Request(new(true, "  Comprobante de un pago: CUIT, monto y fecha.  ", [Lookup(CatalogField.ToString("D").ToUpperInvariant())]));
        Assert.Empty(Errors(valid));
        var normalized = FormDraftPolicy.Normalize(valid);
        Assert.Equal("Comprobante de un pago: CUIT, monto y fecha.", normalized.Draft.DocumentReading!.Recognition);
        // A catalog field goes in its canonical form, so a check compares it by its bytes.
        Assert.Equal(CatalogField.ToString("D"), Assert.Single(normalized.Draft.DocumentReading.Lookups).Field);

        var resolved = FormDraftPolicy.Resolve(normalized, null, FormRetiredIdentities.Empty).Value;
        var saved = Assert.Single(resolved.Draft.DocumentReading!.Lookups);
        Assert.Equal(resolved.Draft.Fields[0].FieldId, saved.QuestionId);
        Assert.Equal(resolved.Draft.Fields[1].FieldId, saved.SourceQuestionId);
        Assert.NotEqual(Guid.Parse(Customer), saved.QuestionId);
    }

    [Fact]
    public void Without_a_reading_nothing_is_written_and_turned_off_it_keeps_what_was_written()
    {
        Assert.Null(FormDraftPolicy.Normalize(Request(null)).Draft.DocumentReading);
        Assert.DoesNotContain("documentReading", FormDraftPolicy.Serialize(FormDraftPolicy.Normalize(Request(null)).Draft), StringComparison.Ordinal);
        // Off, without a text and without lookups, is no reading at all.
        Assert.Null(FormDraftPolicy.Normalize(Request(new(false, "  ", []))).Draft.DocumentReading);

        var off = Request(new(false, "Lo que se escribió antes.", [Lookup()]));
        Assert.Empty(Errors(off));
        var kept = FormDraftPolicy.Normalize(off).Draft.DocumentReading!;
        Assert.Equal((false, "Lo que se escribió antes."), (kept.Enabled, kept.Recognition));
        Assert.Single(kept.Lookups);
    }

    public static TheoryData<string, FormDocumentReading> Invalid => new()
    {
        { "draft.documentReading.recognition", new(true, "", []) },
        { "draft.documentReading.recognition", new(true, "   ", []) },
        { "draft.documentReading.recognition", new(false, new string('x', 1001), []) },
        { "draft.documentReading.recognition", new(true, "Comprobante\0", []) },
        { "draft.documentReading.recognition", new(true, null!, []) },
        { "draft.documentReading.lookups", new(true, "Pago", null!) },
        { "draft.documentReading.lookups", new(true, "Pago", Enumerable.Range(0, 6).Select(_ => Lookup()).ToArray()) },
        { "draft.documentReading.lookups[0]", new(true, "Pago", [null!]) },
        // The question it fills is a catalog question of the draft; what it reads is another question of the draft.
        { "draft.documentReading.lookups[0].questionId", new(true, "Pago", [Lookup(question: TaxId, source: Notes)]) },
        { "draft.documentReading.lookups[0].questionId", new(true, "Pago", [Lookup(question: Guid.CreateVersion7().ToString())]) },
        { "draft.documentReading.lookups[0].sourceQuestionId", new(true, "Pago", [Lookup(source: Customer)]) },
        { "draft.documentReading.lookups[0].sourceQuestionId", new(true, "Pago", [Lookup(source: Guid.CreateVersion7().ToString())]) },
        // A code or a text is compared with a text that was read, not with a number.
        { "draft.documentReading.lookups[0].sourceQuestionId", new(true, "Pago", [Lookup(FormDocumentLookup.Text, source: Amount)]) },
        { "draft.documentReading.lookups[0].field", new(true, "Pago", [Lookup("detail")]) },
        { "draft.documentReading.lookups[0].field", new(true, "Pago", [Lookup(Guid.Empty.ToString())]) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void A_reading_that_breaks_its_rules_is_refused_at_its_path(string path, FormDocumentReading reading) =>
        Assert.Contains(path, Errors(Request(reading)));

    [Fact]
    public void A_recognition_of_a_thousand_characters_and_a_number_compared_with_a_catalog_field_are_accepted()
    {
        Assert.Empty(Errors(Request(new(true, new string('x', 1000), [Lookup(CatalogField.ToString(), source: Amount)]))));
        // A question inside the block of rows is neither filled nor read by a lookup.
        var block = new FormFieldInput("block", null, null, "group", Text, null, true, null, null, Rows: new(1, 10));
        var item = new FormFieldInput(Guid.CreateVersion7().ToString(), null, null, "shortText", Text, null, false, 80, null, Repeated: true);
        Assert.Contains("draft.documentReading.lookups[0].sourceQuestionId",
            Errors(Request(new(true, "Pago", [Lookup(source: item.LocalId)]), [.. Fields(), block, item])));
    }

    [Fact]
    public void The_saved_draft_reads_its_reading_back_and_a_check_matches_only_the_same_reading()
    {
        var reading = new FormDocumentReading(true, "Comprobante de un pago.", []);
        var name = new Dictionary<string, string> { ["es"] = "Pago" };
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Pago", FormDraftPolicy.Serialize(name), "PAG", "es", FormDraftPolicy.Serialize(name),
            FormDraftPolicy.Serialize(new FormQuestionSchema(1, [])), 0, true, documentReadingJson: FormDraftPolicy.Serialize(reading)).IsSuccess);
        var saved = FormDraftPolicy.Definition(definition);
        Assert.Equal(reading.Recognition, saved.Draft.DocumentReading!.Recognition);
        Assert.True(saved.Draft.DocumentReading.Enabled);

        SaveFormDraftRequest Check(FormDocumentReading? sent) => FormDraftPolicy.Normalize(new SaveFormDraftRequest(Guid.NewGuid(), name, "PAG",
            new(1, "es", name, [], DocumentReading: sent)));
        Assert.True(FormPublicationSnapshotPolicy.Matches(Check(reading), saved));
        Assert.False(FormPublicationSnapshotPolicy.Matches(Check(null), saved));
        Assert.False(FormPublicationSnapshotPolicy.Matches(Check(reading with { Recognition = "Otro texto." }), saved));
        // The reading is part of what the publication check seals.
        var without = saved with { Draft = saved.Draft with { DocumentReading = null } };
        Assert.NotEqual(FormPublicationSnapshotPolicy.Fingerprint(saved, []), FormPublicationSnapshotPolicy.Fingerprint(without, []));
        Assert.DoesNotContain("documentReading", FormDraftPolicy.Serialize(without), StringComparison.Ordinal);
    }

    // At publication a lookup by a catalog field compares what a question read with that field: a text with a text, a number (whole
    // or decimal) with a decimal, a date with a date; a yes or no field compares with nothing.
    [Theory]
    [InlineData("text", "shortText", true)]
    [InlineData("text", "longText", true)]
    [InlineData("text", "decimal", false)]
    [InlineData("text", "date", false)]
    [InlineData("integer", "decimal", true)]
    [InlineData("decimal", "decimal", true)]
    [InlineData("integer", "shortText", false)]
    [InlineData("decimal", "longText", false)]
    [InlineData("decimal", "date", false)]
    [InlineData("date", "date", true)]
    [InlineData("date", "shortText", false)]
    [InlineData("date", "decimal", false)]
    [InlineData("boolean", "shortText", false)]
    [InlineData("boolean", "decimal", false)]
    [InlineData("boolean", "date", false)]
    public void A_catalog_field_compares_only_with_a_question_of_its_kind(string catalogField, string question, bool comparable) =>
        Assert.Equal(comparable, FormPublicationGuard.Comparable(catalogField, question));
}
