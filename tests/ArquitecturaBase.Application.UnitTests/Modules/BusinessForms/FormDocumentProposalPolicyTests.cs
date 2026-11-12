using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Task 7, decision 8: formularios validates a document's proposal without AI: marks, fixed validations, catalog lookups, the bank from
// the account by its entity code and the repetitions of the same file or of the standard's duplicate keys.
public sealed class FormDocumentProposalPolicyTests
{
    private const string ValidAccount = "0170099200000000123459";
    private const string Sha = "1111111111111111111111111111111111111111111111111111111111111111";
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Each_read_question_keeps_its_value_and_mark_and_a_fixed_validation_that_fails_stays_marked_with_its_code()
    {
        var f = new Fixture();
        var prepared = await f.Policy.PrepareAsync(f.Proposal(("amount", "150000.5", true), ("account", "0170099200000000123450", false), ("operation", null, false)), Sha, null, Ct);
        var values = Assert.Single(prepared.Candidates).Values.ToDictionary(value => value.FieldId);
        Assert.Equal(("150000.5", FormDocumentMarks.Doubtful), (values[f.Ids["amount"]].Value, values[f.Ids["amount"]].Mark));
        Assert.Equal(("0170099200000000123450", FormDocumentMarks.Invalid, FormRecordErrors.BankAccountInvalid.Code),
            (values[f.Ids["account"]].Value, values[f.Ids["account"]].Mark, values[f.Ids["account"]].ErrorCode));
        Assert.Equal(((string?)null, FormDocumentMarks.NotRead), (values[f.Ids["operation"]].Value, values[f.Ids["operation"]].Mark));
        // The bank of an account that does not pass stays empty and marked.
        Assert.Equal(((string?)null, FormDocumentMarks.NotRead), (values[f.Ids["bank"]].Value, values[f.Ids["bank"]].Mark));
        Assert.Equal(("sure", "comprobante"), (prepared.Outcome, prepared.DocumentType));
    }

    [Fact]
    public async Task The_bank_or_wallet_comes_from_the_account_by_its_entity_code_with_the_option_texts()
    {
        var f = new Fixture();
        var prepared = await f.Policy.PrepareAsync(f.Proposal(("account", ValidAccount, false)), Sha, null, Ct);
        var bank = Assert.Single(prepared.Candidates).Values.Single(value => value.FieldId == f.Ids["bank"]);
        Assert.Equal((f.BbvaId.ToString("D"), FormDocumentMarks.Completed, "Banco BBVA"), (bank.Value, bank.Mark, bank.Text!["es"]));
    }

    [Fact]
    public async Task A_lookup_fills_an_empty_catalog_question_by_code_or_text_with_what_another_question_read()
    {
        var f = new Fixture(lookups: true);
        var prepared = await f.Policy.PrepareAsync(f.Proposal(("taxId", "20-12345678-6", false), ("holder", "acme sa", false)), Sha, null, Ct);
        var values = Assert.Single(prepared.Candidates).Values.ToDictionary(value => value.FieldId);
        Assert.Equal((f.ClientId.ToString("D"), FormDocumentMarks.Completed), (values[f.Ids["client"]].Value, values[f.Ids["client"]].Mark));
        Assert.Equal((f.SupplierId.ToString("D"), FormDocumentMarks.Completed), (values[f.Ids["supplier"]].Value, values[f.Ids["supplier"]].Mark));
    }

    [Fact]
    public async Task A_repetition_of_the_same_file_or_of_every_duplicate_key_says_where_the_document_already_is()
    {
        var f = new Fixture();
        var recordId = Guid.NewGuid();
        f.Documents.Repetitions.Add(new(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), recordId, 123, null));
        var prepared = await f.Policy.PrepareAsync(f.Proposal(("amount", "100", false), ("operation", "778899", false), ("date", "2026-11-01", false)), Sha, null, Ct);
        Assert.Equal((recordId, "REC-000123"), (prepared.Repetition!.RecordId, prepared.Repetition.RecordNumber));
        var query = Assert.Single(f.Documents.RepetitionQueries);
        Assert.Equal((f.Form.DefinitionId, Sha), (query.DefinitionId, query.Sha256));
        Assert.Equal([("operation", "778899"), ("amount", "100"), ("date", "2026-11-01")],
            query.Keys.Select(key => (f.Ids.Single(pair => pair.Value == key.FieldId).Key, key.Value)));
    }

    [Fact]
    public async Task Without_every_duplicate_key_only_the_same_file_counts_and_a_candidate_of_another_version_is_left_out()
    {
        var f = new Fixture();
        await f.Policy.PrepareAsync(f.Proposal(("amount", "100", false)), Sha, null, Ct);
        Assert.Empty(Assert.Single(f.Documents.RepetitionQueries).Keys);
        var stale = f.Proposal(("amount", "100", false));
        var prepared = await f.Policy.PrepareAsync(stale with { Candidates = [stale.Candidates[0] with { VersionId = Guid.NewGuid() }] }, Sha, null, Ct);
        Assert.Equal(("unknown", 0), (prepared.Outcome, prepared.Candidates.Count));
    }

    private sealed class Fixture : IStructuredFormStandardSource
    {
        public FormReceivedDocumentFixture Documents { get; } = new();
        public FormDocumentProposalPolicy Policy { get; }
        public Dictionary<string, Guid> Ids { get; } = [];
        public FormDocumentFormRow Form { get; }
        public Guid BbvaId { get; } = Guid.CreateVersion7();
        public Guid ClientId { get; } = Guid.CreateVersion7();
        public Guid SupplierId { get; } = Guid.CreateVersion7();
        public IReadOnlyList<StructuredFormStandard> Standards { get; }

        public Fixture(bool lookups = false)
        {
            var banks = Guid.CreateVersion7();
            var clients = Guid.CreateVersion7();
            var suppliers = Guid.CreateVersion7();
            var questions = new List<CanonicalFormField>
            {
                Add("amount", FormReceivedDocumentFixture.Question("decimal", "Monto", required: true)),
                Add("date", FormReceivedDocumentFixture.Question("date", "Fecha")),
                Add("operation", FormReceivedDocumentFixture.Question("shortText", "Operación")),
                Add("account", FormReceivedDocumentFixture.Question("shortText", "Cuenta", validation: FormQuestionValidation.BankAccount)),
                Add("bank", FormReceivedDocumentFixture.Question("choice", "Banco", catalog: banks)),
                Add("taxId", FormReceivedDocumentFixture.Question("shortText", "CUIT", validation: FormQuestionValidation.TaxId)),
                Add("holder", FormReceivedDocumentFixture.Question("shortText", "Titular")),
                Add("client", FormReceivedDocumentFixture.Question("choice", "Cliente", catalog: clients)),
                Add("supplier", FormReceivedDocumentFixture.Question("choice", "Proveedor", catalog: suppliers)),
            };
            Documents.Items.Add((banks, new(BbvaId, "017", "Banco BBVA", null), new Dictionary<string, string>()));
            Documents.Items.Add((clients, new(ClientId, "20123456786", "Cliente uno", null), new Dictionary<string, string>()));
            Documents.Items.Add((suppliers, new(SupplierId, "S1", "ACME SA", null), new Dictionary<string, string>()));
            Form = Documents.AddForm(questions, standardKey: "test.transfer", bindings: Ids,
                lookups: lookups ? [new(Ids["client"], Ids["taxId"], FormDocumentLookup.Code), new(Ids["supplier"], Ids["holder"], FormDocumentLookup.Text)] : null);
            Standards = [new StructuredFormStandard("test.transfer", 1, new Dictionary<string, string> { ["es"] = "Transferencia" },
                new Dictionary<string, string> { ["es"] = "Transferencia" }, "REC", "{}", new Dictionary<string, string>(),
                Reading: new(new Dictionary<string, string> { ["es"] = "Un comprobante." }, new Dictionary<string, string> { ["es"] = "Recibí {amount}." },
                    ["operation", "amount", "date"], new Dictionary<string, string>(), new Dictionary<string, string> { ["account"] = "bank" }))];
            Policy = new(Documents, Documents, [this]);
        }

        public FormDocumentEntryProposal Proposal(params (string Key, string? Value, bool Doubtful)[] values) =>
            new("proposed", 1, "sure", [new(Form.DefinitionId, Form.VersionId, Form.DefinitionRevision,
                values.Select(value => new FormDocumentEntryValue(Ids[value.Key], Type(value.Key), value.Value, value.Doubtful)).ToArray())], "comprobante");

        private string Type(string key) => JsonSerializer.Deserialize<FormVersionSchema>(Form.SchemaJson, JsonSerializerOptions.Web)!.Questions
            .Single(question => question.FieldId == Ids[key]).Type;

        private CanonicalFormField Add(string key, CanonicalFormField question)
        {
            Ids[key] = question.FieldId;
            return question;
        }
    }
}
