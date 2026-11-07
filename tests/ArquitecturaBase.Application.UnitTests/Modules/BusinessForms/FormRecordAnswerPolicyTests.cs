using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormRecordAnswerPolicyTests
{
    private static readonly Guid SupplierId = Guid.Parse("00000000-0000-7000-8000-0000000000a0");
    private static readonly Guid GroupId = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
    private static readonly Guid ItemId = Guid.Parse("00000000-0000-7000-8000-0000000000a2");
    private static readonly Guid QuantityId = Guid.Parse("00000000-0000-7000-8000-0000000000a3");
    private static readonly Guid NoteId = Guid.Parse("00000000-0000-7000-8000-0000000000a4");
    private static readonly Guid CatalogId = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    private static readonly Guid First = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-7000-8000-000000000002");
    private static readonly Guid Unoffered = Guid.Parse("00000000-0000-7000-8000-000000000009");

    private static string Key(Guid id) => $"field_{id:N}";
    private static CanonicalFormField Field(Guid id, string type, bool required, FormRowSettings? rows = null, bool? repeated = null,
        Guid? catalog = null) => new(id, Key(id), type, new Dictionary<string, string> { ["es"] = "Pregunta" }, null, required,
            type is "shortText" or "longText" ? 100 : null, null, catalog is { } value ? new FormQuestionSource(value) : null,
            Rows: rows, Repeated: repeated);
    private static FormVersionSchema Schema(int minimum = 1, int maximum = 3) => new(1,
    [
        Field(SupplierId, "shortText", true),
        Field(GroupId, "group", minimum > 0, new(minimum, maximum)),
        Field(ItemId, "choice", true, repeated: true, catalog: CatalogId),
        Field(QuantityId, "decimal", true, repeated: true),
        Field(NoteId, "shortText", false, repeated: true),
    ]);
    private static IReadOnlyList<StructuredFormCatalogOffer> Offers =>
        [new(ItemId, Key(ItemId), "Artículo", CatalogId, 3, "Manual", "Artículos", [new(First, "Primero", null), new(Second, "Segundo", null)])];
    private static Dictionary<string, string> Item(Guid item, string quantity, string? note = null)
    {
        var answers = new Dictionary<string, string> { [Key(ItemId)] = item.ToString("D"), [Key(QuantityId)] = quantity };
        if (note is not null) answers[Key(NoteId)] = note;
        return answers;
    }
    private static Dictionary<string, string> Answers(string? items) => items is null
        ? new() { [Key(SupplierId)] = "Proveedor" }
        : new() { [Key(SupplierId)] = "Proveedor", [Key(GroupId)] = items };
    private static string Items(params Dictionary<string, string>[] items) => JsonSerializer.Serialize(items);
    private static IReadOnlyList<FormRecordAnswer>? Normalize(string? items, int minimum = 1, int maximum = 3) =>
        FormRecordAnswerPolicy.Normalize(Schema(minimum, maximum), Answers(items), Offers);

    [Fact]
    public void Each_item_keeps_its_answers_in_the_order_of_the_questions_of_the_block()
    {
        var reversed = new Dictionary<string, string> { [Key(QuantityId)] = "100", [Key(ItemId)] = First.ToString("D") };

        var answers = Normalize(Items(Item(Second, "200.5", "Sin daños"), reversed));

        Assert.NotNull(answers);
        Assert.Equal([SupplierId, GroupId], answers.Select(answer => answer.FieldId));
        var rows = answers[1].Rows!;
        Assert.Equal("group", answers[1].Type);
        Assert.Equal(2, rows.Count);
        Assert.Equal([ItemId, QuantityId, NoteId], rows[0].Select(answer => answer.FieldId));
        Assert.Equal(Second, rows[0][0].ChoiceId);
        Assert.Equal(200.5m, rows[0][1].Decimal);
        Assert.Equal("Sin daños", rows[0][2].Text);
        Assert.Equal([ItemId, QuantityId], rows[1].Select(answer => answer.FieldId));
        Assert.Equal(First, rows[1][0].ChoiceId);
        Assert.Equal(100m, rows[1][1].Decimal);
    }

    [Fact]
    public void A_block_without_items_is_an_empty_list_when_its_minimum_is_zero()
    {
        var answers = Normalize("[]", minimum: 0);

        Assert.NotNull(answers);
        Assert.Empty(answers.Single(answer => answer.FieldId == GroupId).Rows!);
    }

    [Fact]
    public void An_omitted_block_has_no_entry_only_when_its_minimum_is_zero()
    {
        var optional = Normalize(null, minimum: 0);

        Assert.NotNull(optional);
        Assert.Equal([SupplierId], optional.Select(answer => answer.FieldId));
        Assert.Null(Normalize(null, minimum: 1));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void The_number_of_items_stays_between_the_minimum_and_the_maximum_of_the_block(int count, bool valid)
    {
        var items = Items(Enumerable.Range(0, count).Select(_ => Item(First, "1")).ToArray());

        Assert.Equal(valid, Normalize(items, minimum: 2, maximum: 3) is not null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_item_without_a_required_answer_is_not_valid(string? quantity)
    {
        var item = new Dictionary<string, string> { [Key(ItemId)] = First.ToString("D") };
        if (quantity is not null) item[Key(QuantityId)] = quantity;

        Assert.Null(Normalize(Items(Item(Second, "1"), item)));
    }

    [Fact]
    public void An_optional_question_of_an_item_can_stay_empty()
    {
        var answers = Normalize(Items(Item(First, "1", "")));

        Assert.NotNull(answers);
        Assert.Equal([ItemId, QuantityId], answers[1].Rows![0].Select(answer => answer.FieldId));
    }

    [Theory]
    [InlineData("supplier")]
    [InlineData("group")]
    [InlineData("unknown")]
    public void An_item_accepts_only_the_questions_of_its_block(string key)
    {
        var item = Item(First, "1");
        item[key switch { "supplier" => Key(SupplierId), "group" => Key(GroupId), _ => "field_unknown" }] = "x";

        Assert.Null(Normalize(Items(item)));
    }

    [Fact]
    public void An_item_answers_each_question_once()
    {
        var duplicate = $$"""[{"{{Key(ItemId)}}":"{{First:D}}","{{Key(QuantityId)}}":"1","{{Key(QuantityId)}}":"2"}]""";

        Assert.Null(Normalize(duplicate));
    }

    [Fact]
    public void A_question_of_the_block_is_not_answered_outside_an_item()
    {
        var answers = Answers(Items(Item(First, "1")));
        answers[Key(QuantityId)] = "1";

        Assert.Null(FormRecordAnswerPolicy.Normalize(Schema(), answers, Offers));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("[[]]")]
    [InlineData("[null]")]
    [InlineData("not json")]
    public void The_block_is_a_list_of_items(string items) => Assert.Null(Normalize(items));

    [Fact]
    public void The_answers_of_an_item_are_text()
    {
        var number = $$"""[{"{{Key(ItemId)}}":"{{First:D}}","{{Key(QuantityId)}}":1}]""";

        Assert.Null(Normalize(number));
    }

    [Fact]
    public void An_option_of_an_item_has_to_be_offered() => Assert.Null(Normalize(Items(Item(Unoffered, "1"))));

    [Theory]
    [InlineData("1,5")]
    [InlineData("1e3")]
    [InlineData("abc")]
    public void An_item_keeps_the_canonical_values_of_a_question_outside_the_block(string quantity) =>
        Assert.Null(Normalize(Items(Item(First, quantity))));
}
