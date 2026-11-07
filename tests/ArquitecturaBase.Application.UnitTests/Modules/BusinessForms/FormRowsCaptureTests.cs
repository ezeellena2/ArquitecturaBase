using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormRowsCaptureTests
{
    private static readonly Guid SupplierId = Guid.Parse("00000000-0000-7000-8000-0000000000a0");
    private static readonly Guid GroupId = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
    private static readonly Guid ItemId = Guid.Parse("00000000-0000-7000-8000-0000000000a2");
    private static readonly Guid QuantityId = Guid.Parse("00000000-0000-7000-8000-0000000000a3");
    private static readonly Guid CatalogId = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    private static readonly Guid First = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-7000-8000-000000000002");
    private static readonly Guid Third = Guid.Parse("00000000-0000-7000-8000-000000000003");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key(Guid id) => $"field_{id:N}";
    private static CanonicalFormField Field(Guid id, string type, bool required, FormRowSettings? rows = null, bool? repeated = null,
        Guid? catalog = null) => new(id, Key(id), type, new Dictionary<string, string> { ["es"] = "Pregunta" }, null, required,
            type == "shortText" ? 100 : null, null, catalog is { } value ? new FormQuestionSource(value) : null, Rows: rows, Repeated: repeated);
    private static BusinessFormCaptureFixture Block(int minimum = 0, int maximum = 3) => new(
    [
        Field(SupplierId, "shortText", true),
        Field(GroupId, "group", minimum > 0, new(minimum, maximum)),
        Field(ItemId, "choice", true, repeated: true, catalog: CatalogId),
        Field(QuantityId, "decimal", true, repeated: true),
    ]);
    private static IReadOnlyList<StructuredFormCatalogOffer> Offers =>
        [new(ItemId, Key(ItemId), "Artículo", CatalogId, 3, "Manual", "Artículos",
            [new(First, "Primero", null), new(Second, "Segundo", null), new(Third, "Tercero", null)])];
    private static Dictionary<string, string> Item(Guid item, string quantity) =>
        new() { [Key(ItemId)] = item.ToString("D"), [Key(QuantityId)] = quantity };
    private static StructuredRecordCaptureRequest Request(BusinessFormCaptureFixture f, params Dictionary<string, string>[] items) =>
        f.Request(new Dictionary<string, string> { [Key(SupplierId)] = "Proveedor", [Key(GroupId)] = JsonSerializer.Serialize(items) })
            with { Catalogs = Offers };

    [Fact]
    public async Task Three_items_store_a_value_and_a_reference_per_answered_question_of_each_item()
    {
        var f = Block();

        var result = await f.Sink.CaptureAsync(Request(f, Item(Third, "200.5"), Item(First, "100"), Item(Second, "7")), Ct);

        Assert.Equal("Completed", result.State);
        var record = Assert.Single(f.Repository.Records);
        Assert.Equal($$"""[{"fieldId":"{{SupplierId}}","value":"Proveedor"},{"fieldId":"{{GroupId}}","value":[[{"fieldId":"{{ItemId}}","value":"{{Third}}"},{"fieldId":"{{QuantityId}}","value":200.5}],[{"fieldId":"{{ItemId}}","value":"{{First}}"},{"fieldId":"{{QuantityId}}","value":100}],[{"fieldId":"{{ItemId}}","value":"{{Second}}"},{"fieldId":"{{QuantityId}}","value":7}]]}]""",
            record.ValuesJson);
        Assert.Equal(0, Assert.Single(f.Repository.Values, value => value.FieldId == SupplierId).Row);
        var choices = f.Repository.Values.Where(value => value.FieldId == ItemId).OrderBy(value => value.Row).ToArray();
        Assert.Equal([0, 1, 2], choices.Select(value => value.Row));
        Assert.Equal([Third, First, Second], choices.Select(value => value.ChoiceId!.Value));
        var quantities = f.Repository.Values.Where(value => value.FieldId == QuantityId).OrderBy(value => value.Row).ToArray();
        Assert.Equal([200.5m, 100m, 7m], quantities.Select(value => value.DecimalValue!.Value));
        Assert.All(f.Repository.Values, value => Assert.Equal(0, value.Position));
        Assert.DoesNotContain(f.Repository.Values, value => value.FieldId == GroupId);
        var references = f.Repository.References.OrderBy(reference => reference.Row).ToArray();
        Assert.Equal([0, 1, 2], references.Select(reference => reference.Row));
        Assert.Equal([Third, First, Second], references.Select(reference => reference.ItemId));
        Assert.All(references, reference => Assert.Equal(ItemId, reference.FieldId));
        Assert.Equal(1, f.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task A_block_without_items_is_stored_as_an_empty_list()
    {
        var f = Block(minimum: 0);

        var result = await f.Sink.CaptureAsync(Request(f), Ct);

        Assert.Equal("Completed", result.State);
        Assert.Equal($$"""[{"fieldId":"{{SupplierId}}","value":"Proveedor"},{"fieldId":"{{GroupId}}","value":[]}]""",
            Assert.Single(f.Repository.Records).ValuesJson);
        Assert.Equal([SupplierId], f.Repository.Values.Select(value => value.FieldId));
        Assert.Empty(f.Repository.References);
    }

    [Fact]
    public async Task The_same_option_chosen_in_two_items_has_a_reference_per_item()
    {
        var f = Block();

        var result = await f.Sink.CaptureAsync(Request(f, Item(First, "1"), Item(First, "2")), Ct);

        Assert.Equal("Completed", result.State);
        var references = f.Repository.References.OrderBy(reference => reference.Row).ToArray();
        Assert.Equal([0, 1], references.Select(reference => reference.Row));
        Assert.All(references, reference => Assert.Equal(First, reference.ItemId));
        Assert.Equal([CatalogId], f.Repository.CatalogLocks);
    }

    [Fact]
    public async Task A_replay_with_the_same_items_returns_the_same_record_and_other_items_conflict()
    {
        var f = Block();
        var first = await f.Sink.CaptureAsync(Request(f, Item(First, "1"), Item(Second, "2")), Ct);

        var replay = await f.Sink.CaptureAsync(Request(f, Item(First, "1"), Item(Second, "2")), Ct);
        f.ReceiptReader.SkipRead = true;
        var conflict = await f.Sink.CaptureAsync(Request(f, Item(Second, "2"), Item(First, "1")), Ct);

        Assert.Equal(first, replay);
        Assert.Equal(FormRecordErrors.ReplayConflict.Code, conflict.ErrorCode);
        Assert.Single(f.Repository.Records);
    }

    public static TheoryData<string> InvalidItems => ["below-minimum", "above-maximum", "required-empty", "foreign-key", "repeated-outside"];

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task Invalid_items_are_rejected_without_consuming_a_number(string invalid)
    {
        var f = Block(minimum: 1, maximum: 2);
        var request = invalid switch
        {
            "below-minimum" => Request(f),
            "above-maximum" => Request(f, Item(First, "1"), Item(Second, "2"), Item(Third, "3")),
            "required-empty" => Request(f, Item(First, "")),
            "foreign-key" => Request(f, new Dictionary<string, string>(Item(First, "1")) { [Key(SupplierId)] = "Otro" }),
            _ => Request(f, Item(First, "1")) with
            {
                Answers = new Dictionary<string, string>(Request(f, Item(First, "1")).Answers) { [Key(QuantityId)] = "1" },
            },
        };

        var result = await f.Sink.CaptureAsync(request, Ct);

        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.InvalidResponse.Code, result.ErrorCode);
        Assert.Empty(f.Repository.Records);
        Assert.Empty(f.Repository.Values);
        Assert.Equal(0, f.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task An_option_of_an_item_retired_under_the_lock_is_rejected_with_its_item_and_replays_the_same()
    {
        var f = Block();
        f.Repository.InactiveItems.Add(Second);

        var result = await f.Sink.CaptureAsync(Request(f, Item(First, "1"), Item(Second, "2")), Ct);
        var replay = await f.Sink.CaptureAsync(Request(f, Item(First, "1"), Item(Second, "2")), Ct);

        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, result.ErrorCode);
        Assert.Equal(ItemId, result.RejectedFieldId);
        Assert.Equal(1, result.RejectedRow);
        var receipt = Assert.Single(f.Repository.Receipts);
        Assert.Equal(ItemId, receipt.RejectedFieldId);
        Assert.Equal(1, receipt.RejectedRow);
        Assert.Equal(result, replay);
        Assert.Empty(f.Repository.Records);
    }

    [Fact]
    public async Task An_option_retired_outside_a_block_has_no_rejected_item()
    {
        var fixture = new BusinessFormCaptureFixture("choice", true, CatalogId);
        fixture.Repository.InactiveItems.Add(First);
        var offer = new StructuredFormCatalogOffer(Guid.ParseExact(fixture.Key[6..], "N"), fixture.Key, "Artículo", CatalogId, 3, "Manual",
            "Artículos", [new(First, "Primero", null)]);

        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string> { [fixture.Key] = First.ToString("D") })
            with { Catalogs = [offer] }, Ct);

        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, result.ErrorCode);
        Assert.Null(result.RejectedRow);
        Assert.Null(Assert.Single(fixture.Repository.Receipts).RejectedRow);
    }

    [Fact]
    public async Task A_capture_without_a_block_keeps_the_fingerprint_of_its_receipts()
    {
        // The fingerprint reads only the request: an unknown version is rejected and its receipt keeps it, as before blocks.
        var f = new BusinessFormCaptureFixture("shortText", true);
        var answers = new Dictionary<string, string> { ["field_b"] = "Norte", ["field_a"] = "12.5" };
        StructuredRecordCaptureRequest Unknown(string sourceReceipt) => new(Guid.Parse(sourceReceipt),
            Guid.Parse("00000000-0000-7000-8000-0000000000d1"), Guid.Parse("00000000-0000-7000-8000-0000000000e1"),
            Guid.Parse("00000000-0000-7000-8000-0000000000f1"), f.Request(answers).SubmittedAtUtc, answers);

        await f.Sink.CaptureAsync(Unknown("00000000-0000-7000-8000-000000000201"), Ct);
        await f.Sink.CaptureEntryAsync(Unknown("00000000-0000-7000-8000-000000000202"), Ct);

        var fingerprints = f.Repository.Receipts.OrderBy(receipt => receipt.SourceReceiptId).Select(receipt => receipt.Fingerprint).ToArray();
        Assert.Equal(["0883139731d21168ed97c1ba8db6964ed44ce658c2bc36f7edd6d3a110726038", "6559e7ecd38c46770dfcf4091cfe7e2f95afb4f0412c6a7544b86fbadffecf1b"], fingerprints);
    }
}
