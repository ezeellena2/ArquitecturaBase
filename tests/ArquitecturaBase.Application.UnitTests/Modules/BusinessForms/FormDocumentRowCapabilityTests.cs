using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// VC1-A, task 27: the rows of a spreadsheet are proposed with a mapping (decision 9). The candidates read also goes with a spreadsheet, and
// forms.documents.propose-rows@1 only with one prepared to load: it takes only the forms the latest documents read of the turn showed, the
// rows of data the model saw in the sample and the columns their sheet shows; the server adds the rows it saw, and a problem goes back with
// its path to be proposed again.
public sealed class FormDocumentRowCapabilityTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public void The_candidates_read_also_goes_with_a_spreadsheet_and_propose_rows_only_with_one()
    {
        var declared = new Fixture().Source.Definitions;
        var definitions = declared.ToDictionary(definition => definition.Id);

        Assert.Equal([BusinessFormDocumentAssistantCapabilitySource.CandidatesId, BusinessFormDocumentAssistantCapabilitySource.FormId,
            BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId, BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId], declared.Select(definition => definition.Id));
        AssistantAttachmentKind[] files = [AssistantAttachmentKind.None, AssistantAttachmentKind.Spreadsheet, AssistantAttachmentKind.Document];
        Assert.Equal([false, true, true], files.Select(definitions[BusinessFormDocumentAssistantCapabilitySource.CandidatesId].OfferedWith));
        Assert.Equal([false, false, true], files.Select(definitions[BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId].OfferedWith));
        var rows = definitions[BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId];
        Assert.Equal([false, true, false], files.Select(rows.OfferedWith));
        Assert.Equal((AssistantCapabilityKind.Proposal, 1, false, false), (rows.Kind, rows.Version, rows.OfferedInHome, rows.DraftsText));
        Assert.Empty(rows.RequiredPermissions.Actor.Concat(rows.RequiredPermissions.Subject));
        Assert.Equal(StructuredAttachmentLimits.MaximumLoadingRows, rows.Budget.MaximumItems);
        using var input = JsonDocument.Parse(rows.InputSchemaJson);
        using var output = JsonDocument.Parse(rows.OutputSchemaJson);
        Assert.Equal(["forms", "unmatched", "documentType"], input.RootElement.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        Assert.False(input.RootElement.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task The_rows_are_normalized_with_the_columns_of_their_forms_and_the_rows_the_model_saw()
    {
        var f = new Fixture();
        var read = await f.ReadAsync();

        var result = await f.Source.ExecuteAsync(f.Propose(read, [
                Fixture.Group(f.Payments, "Enero", [3, 2], ("Monto", " b "), ("Fecha", "A"), ("Cliente", "C")) with { Doubtful = ["b"] },
                Fixture.Group(f.Expenses, "Febrero", [2], ("Total", "B"))],
            [new("Enero", [5])]), Ct);

        Assert.Equal("proposed", result.Value.State);
        var proposal = Assert.IsType<FormDocumentRowsProposal>(result.Value.Payload);
        Assert.Equal((1, "planilla de cobranzas"), (proposal.ReadOrdinal, proposal.DocumentType));
        var payments = proposal.Forms[0];
        Assert.Equal((f.Payments.DefinitionId, f.Payments.VersionId, 3L, "Enero"), (payments.DefinitionId, payments.VersionId, payments.ExpectedDefinitionRevision, payments.Sheet));
        Assert.Equal([2, 3], payments.Rows);
        Assert.Equal([("decimal", "B"), ("date", "A"), ("shortText", "C")], payments.Columns.Select(column => (column.Type, column.Column)));
        Assert.Equal(["B"], payments.Doubtful);
        Assert.Equal((f.Expenses.DefinitionId, "Febrero"), (proposal.Forms[1].DefinitionId, proposal.Forms[1].Sheet));
        Assert.Equal([2], proposal.Forms[1].Rows);
        Assert.Equal([("Enero", 5)], proposal.Unmatched.SelectMany(set => set.Rows.Select(row => (set.Sheet, row))));
        // The rows of data the sample showed, never its headers: the server's, whatever the model named.
        Assert.Equal([("Enero", 2), ("Enero", 3), ("Enero", 4), ("Enero", 5), ("Febrero", 2)], proposal.Seen.SelectMany(set => set.Rows.Select(row => (set.Sheet, row))));
        Assert.Equal(4, result.Value.ItemCount);
        var json = JsonSerializer.Serialize(proposal, JsonSerializerOptions.Web);
        var parsed = f.Source.ParseOutput(new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, 1), json);
        Assert.Equal(json, JsonSerializer.Serialize(Assert.IsType<FormDocumentRowsProposal>(parsed.Value), JsonSerializerOptions.Web));
    }

    [Fact]
    public async Task Only_with_a_spreadsheet_prepared_to_load()
    {
        var f = new Fixture();
        var read = await f.ReadAsync();
        var request = f.Propose(read, [Fixture.Group(f.Payments, "Enero", [2], ("Monto", "B"))]);
        var authoring = FormAttachmentPreparationPolicy.Prepare("cobranzas.xlsx", StructuredSpreadsheetKind.Xlsx, 2048,
            new FormSpreadsheet("Enero", Fixture.Sha, [Fixture.Row(1, "Fecha", "Monto"), Fixture.Row(2, "2026-11-03", "95000")])).Spreadsheet!;

        Assert.Equal(FormRecordErrors.InvalidResponse, (await f.Source.ExecuteAsync(request with { Attachment = null }, Ct)).Error);
        Assert.Equal(FormRecordErrors.InvalidResponse, (await f.Source.ExecuteAsync(request with { Attachment = authoring }, Ct)).Error);
        Assert.Equal("proposed", (await f.Source.ExecuteAsync(request, Ct)).Value.State);
    }

    [Fact]
    public async Task Only_the_forms_of_the_latest_read_of_the_turn_and_never_a_read_of_another_person()
    {
        var f = new Fixture();
        var read = await f.ReadAsync();
        var other = f.Documents.AddForm([FormReceivedDocumentFixture.Question("decimal", "Importe")], name: "Otro");

        var unobserved = await f.Source.ExecuteAsync(f.Propose(read, [Fixture.Group(other, "Enero", [2], ("Importe", "B"))]), Ct);
        var unread = await f.Source.ExecuteAsync(f.Propose(null, [Fixture.Group(f.Payments, "Enero", [2], ("Monto", "B"))]), Ct);
        var foreign = await f.Source.ExecuteAsync(f.Propose(read with { Payload = ((FormDocumentReadPayload)read.Payload) with { UserId = Guid.NewGuid() } }, []), Ct);
        f.Permissions.Permissions[f.Sender] = [];
        var revoked = await f.Source.ExecuteAsync(f.Propose(read, [Fixture.Group(f.Payments, "Enero", [2], ("Monto", "B"))]), Ct);

        Assert.Contains(Rejection(unobserved).Issues, issue => issue == new FormDocumentEntryIssue("forms[0]", FormDocumentEntryIssue.NotObserved));
        Assert.Contains(Rejection(unread).Issues, issue => issue.Code == FormDocumentEntryIssue.NotObserved);
        Assert.Equal(FormRecordErrors.InvalidResponse, foreign.Error);
        Assert.Equal(FormRecordErrors.AuthorizationChanged, revoked.Error);
    }

    [Fact]
    public async Task No_form_fits_is_a_proposal_of_unmatched_rows_even_without_a_read()
    {
        var f = new Fixture();

        var result = await f.Source.ExecuteAsync(f.Propose(null, [], [new("Enero", [2, 3, 4])]), Ct);

        var proposal = Assert.IsType<FormDocumentRowsProposal>(result.Value.Payload);
        Assert.Equal((0, 0), (proposal.ReadOrdinal, proposal.Forms.Count));
        Assert.Equal([2, 3, 4], Assert.Single(proposal.Unmatched).Rows);
    }

    [Theory]
    [InlineData("unknown-sheet", "forms[0].sheet", FormDocumentEntryIssue.Invalid)]
    [InlineData("headers", "forms[0].rows[0]", FormDocumentRowCodec.UnknownRow)]
    [InlineData("not-seen", "forms[0].rows[0]", FormDocumentRowCodec.UnknownRow)]
    [InlineData("twice", "unmatched[0].rows[0]", FormDocumentEntryIssue.Repeated)]
    [InlineData("no-rows", "forms[0].rows", FormDocumentEntryIssue.Count)]
    [InlineData("no-columns", "forms[0].columns", FormDocumentEntryIssue.Count)]
    [InlineData("unknown-question", "forms[0].columns[0].fieldId", FormDocumentEntryIssue.UnknownQuestion)]
    [InlineData("catalog", "forms[0].columns[0].fieldId", FormDocumentEntryIssue.FilledByServer)]
    [InlineData("missing-column", "forms[0].columns[0].column", FormDocumentEntryIssue.Invalid)]
    [InlineData("repeated-column", "forms[0].columns[1].column", FormDocumentEntryIssue.Repeated)]
    [InlineData("doubtful", "forms[0].doubtful[0]", FormDocumentEntryIssue.Invalid)]
    [InlineData("form-twice", "forms[1].definitionId", FormDocumentEntryIssue.Repeated)]
    [InlineData("personal", "documentType", FormDocumentEntryIssue.PersonalData)]
    public async Task A_proposal_that_does_not_pass_goes_back_rejected_with_its_path_and_is_never_trimmed(string problem, string path, string code)
    {
        var f = new Fixture(cut: problem == "not-seen");
        var read = await f.ReadAsync();
        var good = Fixture.Group(f.Payments, "Enero", [2], ("Monto", "B"));
        var (forms, unmatched, documentType) = problem switch
        {
            "unknown-sheet" => (new[] { good with { Sheet = "Marzo" } }, Array.Empty<FormDocumentRowSet>(), "planilla"),
            "headers" => ([good with { Rows = [1] }], [], "planilla"),
            "not-seen" => ([good with { Rows = [f.LastRow] }], [], "planilla"),
            "twice" => ([good], [new FormDocumentRowSet("Enero", [2])], "planilla"),
            "no-rows" => ([good with { Rows = [] }], [], "planilla"),
            "no-columns" => ([good with { Columns = [] }], [], "planilla"),
            "unknown-question" => ([good with { Columns = [new(Guid.NewGuid(), "B")] }], [], "planilla"),
            "catalog" => ([Fixture.Group(f.Payments, "Enero", [2], ("Banco", "C"))], [], "planilla"),
            "missing-column" => ([Fixture.Group(f.Payments, "Enero", [2], ("Monto", "H"))], [], "planilla"),
            "repeated-column" => ([Fixture.Group(f.Payments, "Enero", [2], ("Monto", "B"), ("Cliente", "B"))], [], "planilla"),
            "doubtful" => ([good with { Doubtful = ["C"] }], [], "planilla"),
            "form-twice" => ([good, good with { Rows = [3] }], [], "planilla"),
            _ => ([good], [], "cobranzas de 20123456786"),
        };

        var result = await f.Source.ExecuteAsync(f.Propose(read, forms, unmatched, documentType), Ct);

        Assert.Equal("rejected", result.Value.State);
        Assert.Contains(Rejection(result).Issues, issue => issue.Path == path && issue.Code == code);
        var parsed = f.Source.ParseOutput(new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, 1),
            JsonSerializer.Serialize(Rejection(result), JsonSerializerOptions.Web));
        Assert.IsType<FormDocumentEntryRejection>(parsed.Value);
    }

    [Fact]
    public void The_codec_takes_exactly_its_own_shapes()
    {
        Assert.True(FormDocumentRowCodec.ParseArguments("{\"forms\":[],\"unmatched\":[]}").IsFailure);
        Assert.True(FormDocumentRowCodec.ParseArguments("{\"forms\":[],\"unmatched\":[],\"documentType\":\"x\",\"extra\":1}").IsFailure);
        Assert.True(FormDocumentRowCodec.ParseArguments("{\"forms\":[{\"definitionId\":\"" + Guid.NewGuid() + "\"}],\"unmatched\":[],\"documentType\":\"x\"}").IsFailure);
        Assert.True(FormDocumentRowCodec.ParseArguments("not json").IsFailure);
        Assert.True(FormDocumentRowCodec.ParseArguments("{\"forms\":[],\"unmatched\":[],\"documentType\":\"planilla\"}").IsSuccess);
        var source = new Fixture().Source;
        Assert.True(source.ParseOutput(new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, 2), "{}").IsFailure);
        Assert.True(source.ParseOutput(new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, 1), "{\"state\":\"proposed\"}").IsFailure);
        // A row named that the model never saw is not a proposal its owner would have returned.
        var unseen = new FormDocumentRowsProposal("proposed", 0, [], [new("Enero", [9])], [new("Enero", [2])], "planilla");
        Assert.True(source.ParseOutput(new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, 1),
            JsonSerializer.Serialize(unseen, JsonSerializerOptions.Web)).IsFailure);
    }

    // A workbook may bring one sheet for each day: every sheet of the rows seen, or that fit none, takes a row of its own.
    [Fact]
    public async Task More_than_twenty_sheets_are_normalized_and_read_back()
    {
        var f = new Fixture();
        var sample = FormAttachmentPreparationPolicy.PrepareRows("cobranzas.xlsx", StructuredSpreadsheetKind.Xlsx, 2048, [.. Enumerable.Range(1, 25)
            .Select(day => new FormSpreadsheet("Día " + day, Fixture.Sha, [Fixture.Row(1, "Fecha", "Monto"), Fixture.Row(2, "2026-11-01", "100")]))]).Spreadsheet!;

        var result = await f.Source.ExecuteAsync(f.Propose(null, [], [.. Enumerable.Range(1, 25).Select(day => new FormDocumentRowSet("Día " + day, [2]))])
            with { Attachment = sample }, Ct);

        var proposal = Assert.IsType<FormDocumentRowsProposal>(result.Value.Payload);
        Assert.Equal((25, 25, 25), (proposal.Seen.Count, proposal.Unmatched.Count, result.Value.ItemCount));
        Assert.IsType<FormDocumentRowsProposal>(f.Source.ParseOutput(new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, 1),
            JsonSerializer.Serialize(proposal, JsonSerializerOptions.Web)).Value);
    }

    // A row whose cells could not be read shows the model nothing: it is not seen, so it is never taken as a total or a note.
    [Fact]
    public async Task A_row_without_a_visible_cell_is_not_seen()
    {
        var f = new Fixture();
        var read = await f.ReadAsync();
        var sample = FormAttachmentPreparationPolicy.PrepareRows("cobranzas.xlsx", StructuredSpreadsheetKind.Xlsx, 2048, [new FormSpreadsheet("Enero", Fixture.Sha,
            [Fixture.Row(1, "Fecha", "Monto"), Fixture.Row(2, "2026-11-03", "95000"), new(3, [new FormSpreadsheetCell(null, FormCatalogImportErrors.FormulaValueMissing)])])])
            .Spreadsheet!;

        var unseen = await f.Source.ExecuteAsync(f.Propose(read, [Fixture.Group(f.Payments, "Enero", [3], ("Monto", "B"))]) with { Attachment = sample }, Ct);
        var seen = await f.Source.ExecuteAsync(f.Propose(read, [Fixture.Group(f.Payments, "Enero", [2], ("Monto", "B"))]) with { Attachment = sample }, Ct);

        Assert.Equal((3, 0), (sample.Rows.Count, sample.Rows[2].Cells.Count));
        Assert.Contains(Rejection(unseen).Issues, issue => issue == new FormDocumentEntryIssue("forms[0].rows[0]", FormDocumentRowCodec.UnknownRow));
        Assert.Equal([2], Assert.Single(Assert.IsType<FormDocumentRowsProposal>(seen.Value.Payload).Seen).Rows);
    }

    // Whoever registers the rows checks the proposal the engine returned, as with a document: its forms are in the latest read of its turn.
    [Fact]
    public async Task A_returned_proposal_is_authentic_only_with_its_forms_in_the_latest_read_of_its_turn()
    {
        var f = new Fixture();
        var read = await f.ReadAsync();
        var proposal = Assert.IsType<FormDocumentRowsProposal>((await f.Source.ExecuteAsync(
            f.Propose(read, [Fixture.Group(f.Payments, "Enero", [2], ("Monto", "B"))]), Ct)).Value.Payload);
        var observed = new AssistantObservedProposal(f.TurnId, Guid.NewGuid(), new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, 1), proposal)
        {
            ObservedReads = [read],
        };
        var later = read with { Ordinal = 2, Payload = ((FormDocumentReadPayload)read.Payload) with { Forms = [] } };
        var none = new FormDocumentRowsProposal("proposed", 0, [], [new("Enero", [2])], proposal.Seen, "planilla de cobranzas");

        Assert.True(FormDocumentRowCodec.Authentic(observed, out var authentic));
        Assert.Same(proposal, authentic);
        Assert.False(FormDocumentRowCodec.Authentic(observed with { ObservedReads = [read, later] }, out _));
        Assert.False(FormDocumentRowCodec.Authentic(observed with { ObservedReads = [] }, out _));
        Assert.False(FormDocumentRowCodec.Authentic(observed with { Capability = new(BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId, 1) }, out _));
        // With no form, nothing fits: no read is needed.
        Assert.True(FormDocumentRowCodec.Authentic(new AssistantObservedProposal(f.TurnId, Guid.NewGuid(),
            new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, 1), none), out _));
    }

    private static FormDocumentEntryRejection Rejection(ArquitecturaBase.Domain.Results.Result<AssistantCapabilityResponse> result) =>
        Assert.IsType<FormDocumentEntryRejection>(result.Value.Payload);

    private sealed class Fixture
    {
        public const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
        public FormReceivedDocumentFixture Documents { get; } = new();
        public InMemoryUserAccounts Users { get; } = new();
        public TestDoubles.Auth.FakePermissionService Permissions { get; } = new();
        public BusinessFormDocumentAssistantCapabilitySource Source { get; }
        public Guid Sender { get; }
        public Guid TurnId { get; } = Guid.CreateVersion7();
        public FormDocumentFormRow Payments { get; }
        public FormDocumentFormRow Expenses { get; }
        public StructuredSpreadsheet Sample { get; }
        public int LastRow { get; }

        // «Cobranzas» and «Gastos», both with reading on, and a spreadsheet with «Enero» (headers in row 1, four rows of data) and «Febrero»
        // (one); cut, «Enero» has many more rows, wider than the sample can show, so its last rows are never seen.
        public Fixture(bool cut = false)
        {
            Sender = Users.AddUser("sender@example.invalid").Id;
            Permissions.Permissions[Sender] = [BusinessFormsPermissions.RecordsCreate];
            var banks = Guid.CreateVersion7();
            Documents.Catalogs.Add(new(banks, "{\"es\":\"Bancos\",\"en\":\"Banks\"}", "es"));
            Payments = Documents.AddForm([FormReceivedDocumentFixture.Question("decimal", "Monto", required: true), FormReceivedDocumentFixture.Question("date", "Fecha"),
                FormReceivedDocumentFixture.Question("shortText", "Cliente"), FormReceivedDocumentFixture.Question("choice", "Banco", catalog: banks)], name: "Cobranzas");
            Expenses = Documents.AddForm([FormReceivedDocumentFixture.Question("decimal", "Total")], name: "Gastos");
            Source = new(Documents, Users, Permissions);
            var wide = new string('x', 300);
            FormSpreadsheetRow[] january = cut
                ? [Row(1, "Fecha", "Monto", "Cliente"), .. Enumerable.Range(2, 40).Select(number => Row(number, [.. Enumerable.Repeat(wide, 64)]))]
                : [Row(1, "Fecha", "Monto", "Cliente"), Row(2, "2026-11-03", "95000", "Norte"), Row(3, "2026-11-04", "12500", "Sur"), Row(4, "Total", "107500", null),
                    Row(5, "Ver nota", null, null)];
            LastRow = january[^1].Number;
            var preparation = FormAttachmentPreparationPolicy.PrepareRows("cobranzas.xlsx", StructuredSpreadsheetKind.Xlsx, 2048,
                [new FormSpreadsheet("Enero", Sha, january), new FormSpreadsheet("Febrero", Sha, [Row(1, "Fecha", "Total"), Row(2, "2026-02-01", "8000")])]);
            Sample = preparation.Spreadsheet!;
            if (cut) Assert.DoesNotContain(Sample.Rows, row => row.Sheet == "Enero" && row.Number == LastRow);
        }

        public static FormSpreadsheetRow Row(int number, params string?[] cells) => new(number, [.. cells.Select(text => new FormSpreadsheetCell(text))]);

        public AssistantCapabilityRequest Candidates() => new(TurnId, Context(), new(BusinessFormDocumentAssistantCapabilitySource.CandidatesId, 1), "{}", [])
        {
            Attachment = Sample,
        };

        public async Task<AssistantObservedRead> ReadAsync() =>
            new(TurnId, 1, new(BusinessFormDocumentAssistantCapabilitySource.CandidatesId, 1), (await Source.ExecuteAsync(Candidates(), Ct)).Value.Payload);

        public static FormDocumentRowFormArgument Group(FormDocumentFormRow form, string sheet, IReadOnlyList<int> rows, params (string Label, string Column)[] columns)
        {
            var schema = JsonSerializer.Deserialize<FormVersionSchema>(form.SchemaJson, JsonSerializerOptions.Web)!;
            return new(form.DefinitionId, form.VersionId, sheet, rows,
                columns.Select(column => new FormDocumentRowColumnArgument(schema.Questions.First(question => question.Label["es"] == column.Label).FieldId, column.Column)).ToArray(), []);
        }

        public AssistantCapabilityRequest Propose(AssistantObservedRead? read, IReadOnlyList<FormDocumentRowFormArgument> forms,
            IReadOnlyList<FormDocumentRowSet>? unmatched = null, string documentType = "planilla de cobranzas") =>
            new(TurnId, Context(), new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, 1),
                JsonSerializer.Serialize(new FormDocumentRowsArguments(forms, unmatched ?? [], documentType), JsonSerializerOptions.Web), read is null ? [] : [read])
            {
                Attachment = Sample,
            };

        private AssistantChannelContext Context() => new("whatsapp-assistant", Sender, Sender, "es", "America/Argentina/Buenos_Aires",
            new([], []), [], [new("assistant_proposal", 1)]);
    }
}
