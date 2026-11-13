using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// VC1-A, task 27: formularios arms each row of a spreadsheet from its copy with the mapping the model proposed, without AI (decision 9):
// each cell converted to the type of its question, the fixed validations, the rows repeated by a response or by an earlier row, the
// limit of three loads in the order of the spreadsheet, the rows that fit no form and those the model did not see.
public sealed class FormDocumentRowPolicyTests
{
    private const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Rows_of_two_forms_are_armed_from_the_copy_with_each_cell_converted_to_the_type_of_its_question()
    {
        var f = new Fixture();
        var rows = FormDocumentRowPolicy.DataRows([
            Sheet("Enero", Row(1, "Fecha", "Monto", "Cliente", "Medio", "Factura", "CUIT"),
                Row(2, "03/11/2026", "95.000,50", "Ferretería Norte", " transferencia ", "Sí", "20-12345678-6")),
            Sheet("Transferencias", Row(1, "Operación", "Monto", "Fecha"), Row(2, "778899", "1500", "2026-11-04"))]);
        var proposal = Fixture.Proposal([f.Payments("Enero", [2], "C"), f.Transfers("Transferencias", [2])], seen: [new("Enero", [2]), new("Transferencias", [2])]);

        var plan = await f.Policy.PrepareAsync(proposal, rows, "es", Ct);

        Assert.True(plan.AnyFits);
        Assert.Equal([("Enero", 2, FormDocumentRowOutcome.Load), ("Transferencias", 2, FormDocumentRowOutcome.Load)],
            plan.Rows.Select(row => (row.Sheet, row.Number, row.Outcome)));
        var payment = plan.Rows[0].Candidate!;
        Assert.Equal((f.PaymentsForm.DefinitionId, f.PaymentsForm.VersionId), (payment.DefinitionId, payment.VersionId));
        var values = payment.Values.ToDictionary(value => value.FieldId);
        Assert.Equal(("95000.5", (string?)null), (values[f.Ids["amount"]].Value, values[f.Ids["amount"]].Mark));
        Assert.Equal("2026-11-03", values[f.Ids["date"]].Value);
        Assert.Equal(("Ferretería Norte", FormDocumentMarks.Doubtful), (values[f.Ids["client"]].Value, values[f.Ids["client"]].Mark));
        Assert.Equal((f.Transfer.ToString("D"), "Transferencia"), (values[f.Ids["method"]].Value, values[f.Ids["method"]].Text!["es"]));
        Assert.Equal("true", values[f.Ids["invoice"]].Value);
        Assert.Equal(("20-12345678-6", (string?)null), (values[f.Ids["taxId"]].Value, values[f.Ids["taxId"]].Mark));
        Assert.Equal(["778899", "1500", "2026-11-04"], plan.Rows[1].Candidate!.Values.Select(value => value.Value));
        // The summary of a load is its first two data, in the order of the form.
        Assert.Equal(["95000.5", "2026-11-03"], FormDocumentRowPolicy.Summary(payment).Select(value => value.Value));
        Assert.Equal("planilla de cobranzas", plan.DocumentType);
    }

    [Fact]
    public async Task Three_rows_load_in_the_order_of_the_spreadsheet_and_the_rest_go_to_the_team()
    {
        var f = new Fixture();
        var rows = FormDocumentRowPolicy.DataRows([
            Sheet("Enero", Row(1, "Fecha", "Monto"), Row(2, "2026-11-03", "100"), Row(3, "2026-11-04", "200")),
            Sheet("Febrero", Row(1, "Fecha", "Monto"), Row(2, "2026-02-01", "300"), Row(3, "2026-02-02", "400"), Row(4, "2026-02-03", "500"))]);
        // The model named the second sheet first: the order is the spreadsheet's.
        var proposal = Fixture.Proposal([f.Payments("Febrero", [2, 3, 4]), f.Payments("Enero", [2, 3])],
            seen: [new("Enero", [2, 3]), new("Febrero", [2, 3, 4])]);

        var plan = await f.Policy.PrepareAsync(proposal, rows, "es", Ct);

        Assert.Equal([("Enero", 2, FormDocumentRowOutcome.Load), ("Enero", 3, FormDocumentRowOutcome.Load), ("Febrero", 2, FormDocumentRowOutcome.Load),
            ("Febrero", 3, FormDocumentRowOutcome.OverLimit), ("Febrero", 4, FormDocumentRowOutcome.OverLimit)], plan.Rows.Select(row => (row.Sheet, row.Number, row.Outcome)));
        Assert.All(plan.Rows, row => Assert.NotNull(row.Candidate));
        Assert.Equal(["500", "2026-02-03"], FormDocumentRowPolicy.Summary(plan.Rows[4].Candidate!).Select(value => value.Value));
    }

    [Fact]
    public async Task A_row_with_the_data_of_a_response_is_repeated_and_does_not_count_for_the_limit()
    {
        var f = new Fixture();
        var recordId = Guid.NewGuid();
        f.Documents.Repetitions.Add(new(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), recordId, 123, null));
        var rows = FormDocumentRowPolicy.DataRows([Sheet("Transferencias", Row(1, "Operación", "Monto", "Fecha"),
            Row(2, "778899", "1500", "2026-11-04"), Row(3, null, "200", "2026-11-05"), Row(4, "112233", "300", null), Row(5, "445566", "400", "x"))]);
        var proposal = Fixture.Proposal([f.Transfers("Transferencias", [2, 3, 4, 5])], seen: [new("Transferencias", [2, 3, 4, 5])]);

        var plan = await f.Policy.PrepareAsync(proposal, rows, "es", Ct);

        Assert.Equal([FormDocumentRowOutcome.Repeated, FormDocumentRowOutcome.Load, FormDocumentRowOutcome.Load, FormDocumentRowOutcome.Load],
            plan.Rows.Select(row => row.Outcome));
        Assert.Equal((recordId, "REC-000123"), (plan.Rows[0].Repetition!.RecordId, plan.Rows[0].Repetition!.RecordNumber));
        Assert.NotNull(plan.Rows[0].Candidate);
        // Only the row with every duplicate key asks, by its data alone: a row never compares the file, which it shares with the others.
        var query = Assert.Single(f.Documents.RepetitionQueries);
        Assert.Equal((f.TransfersForm.DefinitionId, (string?)null), (query.DefinitionId, query.Sha256));
        Assert.Equal([("operation", "778899"), ("amount2", "1500"), ("date2", "2026-11-04")],
            query.Keys.Select(key => (f.Ids.Single(pair => pair.Value == key.FieldId).Key, key.Value)));
    }

    [Fact]
    public async Task A_row_equal_to_an_earlier_one_of_its_form_is_repeated_where_that_one_is_and_does_not_count_for_the_limit()
    {
        var f = new Fixture();
        var rows = FormDocumentRowPolicy.DataRows([Sheet("Enero", Row(1, "Fecha", "Monto", "Nota"),
            Row(2, "2026-11-03", "100", "pagó"), Row(3, "2026-11-03", "100", " pagó "), Row(4, "2026-11-03", "100", "otra"), Row(5, "2026-11-04", "200", null),
            Row(6, "2026-11-05", "300", null))]);
        var proposal = Fixture.Proposal([f.Payments("Enero", [2, 3, 4, 5, 6])], seen: [new("Enero", [2, 3, 4, 5, 6])]);

        var plan = await f.Policy.PrepareAsync(proposal, rows, "es", Ct);

        Assert.Equal([FormDocumentRowOutcome.Load, FormDocumentRowOutcome.Repeated, FormDocumentRowOutcome.Load, FormDocumentRowOutcome.Load,
            FormDocumentRowOutcome.OverLimit], plan.Rows.Select(row => row.Outcome));
        Assert.Equal(new FormDocumentRowReference("Enero", 2), plan.Rows[1].SameAs);
        Assert.Null(plan.Rows[1].Repetition);
        Assert.Empty(f.Documents.RepetitionQueries);
    }

    [Fact]
    public async Task A_row_that_fits_no_form_is_unmatched_one_the_model_did_not_see_unseen_and_one_it_did_not_name_is_no_row()
    {
        var f = new Fixture();
        var rows = FormDocumentRowPolicy.DataRows([Sheet("Enero", Row(1, "Fecha", "Monto"), Row(2, "2026-11-03", "100"), Row(3, "Gasto", "50"),
            Row(4, "Total", "150"), Row(5, "2026-11-09", "75"))]);
        var proposal = Fixture.Proposal([f.Payments("Enero", [2])], unmatched: [new("Enero", [3])], seen: [new("Enero", [2, 3, 4])]);

        var plan = await f.Policy.PrepareAsync(proposal, rows, "es", Ct);

        Assert.Equal([("Enero", 2, FormDocumentRowOutcome.Load), ("Enero", 3, FormDocumentRowOutcome.Unmatched), ("Enero", 5, FormDocumentRowOutcome.Unseen)],
            plan.Rows.Select(row => (row.Sheet, row.Number, row.Outcome)));
        Assert.All(plan.Rows.Skip(1), row => Assert.Null(row.Candidate));
        Assert.True(plan.AnyFits);
    }

    [Fact]
    public async Task When_no_row_fits_a_form_the_spreadsheet_is_not_recognized()
    {
        var f = new Fixture();
        var rows = FormDocumentRowPolicy.DataRows([Sheet("Enero", Row(1, "Producto", "Stock"), Row(2, "Tornillos", "100"), Row(3, "Tuercas", "200"))]);
        var proposal = Fixture.Proposal([], unmatched: [new("Enero", [2, 3])], seen: [new("Enero", [2, 3])]);

        var plan = await f.Policy.PrepareAsync(proposal, rows, "es", Ct);

        Assert.False(plan.AnyFits);
        Assert.Equal([FormDocumentRowOutcome.Unmatched, FormDocumentRowOutcome.Unmatched], plan.Rows.Select(row => row.Outcome));
    }

    [Fact]
    public async Task A_cell_that_does_not_convert_is_not_read_and_a_fixed_validation_that_fails_stays_marked()
    {
        var f = new Fixture();
        var rows = FormDocumentRowPolicy.DataRows([Sheet("Enero", Row(1, "Fecha", "Monto", "Cliente", "Medio", "Factura", "CUIT"),
            Row(2, "31/02/2026", "mucho", new string('x', 121), "Cheque", "tal vez", "20-12345678-0"),
            Row(3, "2026-11-03T10:30:00.0000000", "1.234,5", "Sur", "EFECTIVO", "no", "20123456786"),
            Row(4, "2026-11-04", "1.234", "Norte", null, null, null))]);
        var proposal = Fixture.Proposal([f.Payments("Enero", [2, 3, 4])], seen: [new("Enero", [2, 3, 4])]);

        var plan = await f.Policy.PrepareAsync(proposal, rows, "es", Ct);

        var invalid = plan.Rows[0].Candidate!.Values.ToDictionary(value => value.FieldId);
        Assert.All(new[] { "date", "amount", "client", "method", "invoice" }, key =>
            Assert.Equal(((string?)null, FormDocumentMarks.NotRead), (invalid[f.Ids[key]].Value, invalid[f.Ids[key]].Mark)));
        Assert.Equal(("20-12345678-0", FormDocumentMarks.Invalid, FormRecordErrors.TaxIdInvalid.Code),
            (invalid[f.Ids["taxId"]].Value, invalid[f.Ids["taxId"]].Mark, invalid[f.Ids["taxId"]].ErrorCode));
        // A date with its time is never cut to the day, and an amount is read with the conventions of the person's language.
        var other = plan.Rows[1].Candidate!.Values.ToDictionary(value => value.FieldId);
        Assert.Null(other[f.Ids["date"]].Value);
        Assert.Equal(("1234.5", f.Cash.ToString("D"), "false", (string?)null), (other[f.Ids["amount"]].Value, other[f.Ids["method"]].Value,
            other[f.Ids["invoice"]].Value, other[f.Ids["taxId"]].Mark));
        // «1.234» may be a thousand two hundred thirty-four or one and a fraction: as in the import, an ambiguous amount is not read.
        Assert.Null(plan.Rows[2].Candidate!.Values.Single(value => value.FieldId == f.Ids["amount"]).Value);
        Assert.Equal(FormDocumentRowOutcome.Load, plan.Rows[0].Outcome);
    }

    [Fact]
    public async Task The_rows_of_a_form_that_changed_its_version_or_stopped_being_active_fit_no_form()
    {
        var f = new Fixture();
        var rows = FormDocumentRowPolicy.DataRows([Sheet("Enero", Row(1, "Fecha", "Monto"), Row(2, "2026-11-03", "100"), Row(3, "2026-11-04", "200"))]);
        var stale = f.Payments("Enero", [2]) with { VersionId = Guid.NewGuid() };
        var removed = f.Payments("Enero", [3]) with { DefinitionId = Guid.NewGuid() };
        var proposal = Fixture.Proposal([stale, removed], seen: [new("Enero", [2, 3])]);

        var plan = await f.Policy.PrepareAsync(proposal, rows, "es", Ct);

        Assert.Equal([FormDocumentRowOutcome.Unmatched, FormDocumentRowOutcome.Unmatched], plan.Rows.Select(row => row.Outcome));
        Assert.False(plan.AnyFits);
    }

    private static FormSpreadsheet Sheet(string name, params FormSpreadsheetRow[] rows) => new(name, Sha, rows);

    private static FormSpreadsheetRow Row(int number, params string?[] cells) => new(number, [.. cells.Select(text => new FormSpreadsheetCell(text))]);

    private sealed class Fixture : IStructuredFormStandardSource
    {
        public FormReceivedDocumentFixture Documents { get; } = new();
        public FormDocumentRowPolicy Policy { get; }
        public Dictionary<string, Guid> Ids { get; } = [];
        public FormDocumentFormRow PaymentsForm { get; }
        public FormDocumentFormRow TransfersForm { get; }
        public Guid Transfer { get; } = Guid.CreateVersion7();
        public Guid Cash { get; } = Guid.CreateVersion7();
        public IReadOnlyList<StructuredFormStandard> Standards { get; }

        // «Cobranzas», a form of its own (amount, date, client, method of payment, invoice and CUIT), and a copy of a transfer standard
        // whose duplicate keys are the operation, the amount and the date.
        public Fixture()
        {
            PaymentsForm = Documents.AddForm([
                Add("amount", FormReceivedDocumentFixture.Question("decimal", "Monto", required: true)),
                Add("date", FormReceivedDocumentFixture.Question("date", "Fecha")),
                Add("client", FormReceivedDocumentFixture.Question("shortText", "Cliente")),
                Add("method", FormReceivedDocumentFixture.Question("choice", "Medio", options: [
                    new(Transfer, new Dictionary<string, string> { ["es"] = "Transferencia", ["en"] = "Transfer" }),
                    new(Cash, new Dictionary<string, string> { ["es"] = "Efectivo", ["en"] = "Cash" })])),
                Add("invoice", FormReceivedDocumentFixture.Question("boolean", "Factura")),
                Add("taxId", FormReceivedDocumentFixture.Question("shortText", "CUIT", validation: FormQuestionValidation.TaxId))], name: "Cobranzas");
            var transfers = new Dictionary<string, Guid>();
            TransfersForm = Documents.AddForm([
                Add("operation", FormReceivedDocumentFixture.Question("shortText", "Operación"), transfers),
                Add("amount2", FormReceivedDocumentFixture.Question("decimal", "Monto"), transfers, "amount"),
                Add("date2", FormReceivedDocumentFixture.Question("date", "Fecha"), transfers, "date")], standardKey: "test.transfer", bindings: transfers, name: "Transferencias");
            Standards = [new StructuredFormStandard("test.transfer", 1, new Dictionary<string, string> { ["es"] = "Transferencia" },
                new Dictionary<string, string> { ["es"] = "Transferencia" }, "REC", "{}", new Dictionary<string, string>(),
                Reading: new(new Dictionary<string, string> { ["es"] = "Un comprobante." }, new Dictionary<string, string> { ["es"] = "Recibí {amount}." },
                    ["operation", "amount", "date"], new Dictionary<string, string>(), new Dictionary<string, string>()))];
            Policy = new(Documents, new FormDocumentProposalPolicy(Documents, Documents, [this]));
        }

        // Cobranzas from its columns A to F: date, amount, client, method, invoice and CUIT.
        public FormDocumentRowForm Payments(string sheet, IReadOnlyList<int> rows, params string[] doubtful) => new(PaymentsForm.DefinitionId, PaymentsForm.VersionId,
            PaymentsForm.DefinitionRevision, sheet, rows, [Column("date", "A"), Column("amount", "B"), Column("client", "C"), Column("method", "D"),
                Column("invoice", "E"), Column("taxId", "F")], doubtful);

        // The transfers from their columns A to C: operation, amount and date.
        public FormDocumentRowForm Transfers(string sheet, IReadOnlyList<int> rows) => new(TransfersForm.DefinitionId, TransfersForm.VersionId,
            TransfersForm.DefinitionRevision, sheet, rows, [Column("operation", "A"), Column("amount2", "B"), Column("date2", "C")], []);

        public static FormDocumentRowsProposal Proposal(IReadOnlyList<FormDocumentRowForm> forms, IReadOnlyList<FormDocumentRowSet>? unmatched = null,
            IReadOnlyList<FormDocumentRowSet>? seen = null) => new("proposed", 1, forms, unmatched ?? [], seen ?? [], "planilla de cobranzas");

        private FormDocumentRowColumn Column(string key, string letter) => new(Ids[key], Type(key), letter);

        private string Type(string key) => Documents.Forms.Select(form => JsonSerializer.Deserialize<FormVersionSchema>(form.SchemaJson, JsonSerializerOptions.Web)!)
            .SelectMany(schema => schema.Questions).Single(question => question.FieldId == Ids[key]).Type;

        private CanonicalFormField Add(string key, CanonicalFormField question, Dictionary<string, Guid>? bindings = null, string? datum = null)
        {
            Ids[key] = question.FieldId;
            if (bindings is not null) bindings[datum ?? key] = question.FieldId;
            return question;
        }
    }
}
