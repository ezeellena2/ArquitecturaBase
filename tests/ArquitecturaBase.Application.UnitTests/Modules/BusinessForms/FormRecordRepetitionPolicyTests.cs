using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Task 9b of the documents: two receipts sent together are both proposed, but the second capture finds the first one by the standard's
// duplicate keys, with what the person corrected and as the capture stores it.
public sealed class FormRecordRepetitionPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly FormReceivedDocumentFixture documents = new();
    private readonly CanonicalFormField operation = FormReceivedDocumentFixture.Question("shortText", "Operación");
    private readonly CanonicalFormField amount = FormReceivedDocumentFixture.Question("decimal", "Monto");
    private readonly CanonicalFormField date = FormReceivedDocumentFixture.Question("date", "Fecha");

    [Fact]
    public async Task A_capture_with_the_duplicate_keys_of_a_saved_response_is_a_repetition_with_its_number_and_date()
    {
        var (policy, form, schema) = Arrange();
        var savedAtUtc = new DateTime(2026, 11, 10, 15, 30, 0, DateTimeKind.Utc);
        Save(savedAtUtc, "123456");

        var error = await policy.FindAsync(Request(form, "123456", "1500.5", "2026-11-10"), schema, Guid.CreateVersion7(), Ct);

        Assert.Equal(FormRecordErrors.Repeated.Code, error?.Code);
        Assert.Equal(new StructuredRecordRepetition("REC-000007", savedAtUtc), FormRecordRepetitionPolicy.Describe(error!));
        var query = Assert.Single(documents.RepetitionQueries);
        Assert.Null(query.Sha256);
        Assert.Equal([(operation.FieldId, "shortText", "123456"), (amount.FieldId, "decimal", "1500.5"), (date.FieldId, "date", "2026-11-10")],
            query.Keys.Select(key => (key.FieldId, key.Type, key.Value)));
        Assert.Null(FormRecordRepetitionPolicy.Describe(FormRecordErrors.BankAccountInvalid));
    }

    [Fact]
    public async Task Each_key_is_the_value_the_capture_stores_so_a_text_with_spaces_around_it_repeats_only_the_same_text()
    {
        var (policy, form, schema) = Arrange();
        Save(new DateTime(2026, 11, 10, 15, 30, 0, DateTimeKind.Utc), "123456");
        var spaced = Request(form, " 123456 ", "1500.5", "2026-11-10");

        Assert.Null(await policy.FindAsync(spaced, schema, Guid.CreateVersion7(), Ct));

        // The capture keeps a text as it came (FormRecordAnswerPolicy), and the reader compares what each response stored.
        var stored = FormRecordAnswerPolicy.Normalize(schema, spaced.Answers, [])!.Single(answer => answer.FieldId == operation.FieldId).Text;
        Assert.Equal(" 123456 ", stored);
        Assert.Equal(stored, Assert.Single(documents.RepetitionQueries).Keys[0].Value);
        Save(new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc), " 123456 ");
        Assert.Equal(FormRecordErrors.Repeated.Code, (await policy.FindAsync(spaced, schema, Guid.CreateVersion7(), Ct))?.Code);
    }

    [Fact]
    public async Task Without_every_key_or_without_a_standard_nothing_is_compared()
    {
        var (policy, form, schema) = Arrange();
        documents.Repetitions.Add(new(new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc), Guid.CreateVersion7(), 7, null));
        Assert.Null(await policy.FindAsync(Request(form, "", "1500.5", "2026-11-10"), schema, Guid.CreateVersion7(), Ct));
        var own = documents.AddForm([operation, amount, date]);
        Assert.Null(await policy.FindAsync(Request(own, "123456", "1500.5", "2026-11-10"), schema, Guid.CreateVersion7(), Ct));
        Assert.Empty(documents.RepetitionQueries);
        documents.Repetitions.Clear();
        Assert.Null(await policy.FindAsync(Request(form, "123456", "1500.5", "2026-11-10"), schema, Guid.CreateVersion7(), Ct));
    }

    private (FormRecordRepetitionPolicy Policy, FormDocumentFormRow Form, FormVersionSchema Schema) Arrange()
    {
        documents.Standards.Add(FormReceivedDocumentFixture.Standard("test.transfer", "operation", "amount", "date"));
        var form = documents.AddForm([operation, amount, date], standardKey: "test.transfer",
            bindings: new Dictionary<string, Guid> { ["operation"] = operation.FieldId, ["amount"] = amount.FieldId, ["date"] = date.FieldId });
        return (new FormRecordRepetitionPolicy(documents, new FormDocumentProposalPolicy(documents, documents, [documents])), form,
            new FormVersionSchema(1, [operation, amount, date]));
    }

    // A response saved with this operation, the same amount and the same date.
    private void Save(DateTime atUtc, string operationValue) => documents.SavedKeys.Add((new(atUtc, Guid.CreateVersion7(), 7, null),
        [new(operation.FieldId, "shortText", operationValue), new(amount.FieldId, "decimal", "1500.5"), new(date.FieldId, "date", "2026-11-10")]));

    private StructuredRecordCaptureRequest Request(FormDocumentFormRow form, string operationValue, string amountValue, string dateValue) =>
        new(Guid.CreateVersion7(), form.DefinitionId, form.VersionId, Guid.CreateVersion7(), new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc), new Dictionary<string, string>
        {
            [operation.Key] = operationValue, [amount.Key] = amountValue, [date.Key] = dateValue,
        });
}
