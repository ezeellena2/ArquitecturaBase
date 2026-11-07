using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppFlowRowsCodecTests
{
    private static readonly Guid SupplierId = Guid.Parse("00000000-0000-7000-8000-0000000000b0");
    private static readonly Guid GroupId = Guid.Parse("00000000-0000-7000-8000-0000000000b1");
    private static readonly Guid ItemId = Guid.Parse("00000000-0000-7000-8000-0000000000b2");
    private static readonly Guid QuantityId = Guid.Parse("00000000-0000-7000-8000-0000000000b3");
    private static readonly Guid NoteId = Guid.Parse("00000000-0000-7000-8000-0000000000b4");
    private static readonly Guid CatalogId = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    private static readonly Guid First = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-7000-8000-000000000002");

    private static string Key(Guid id) => "field_" + id.ToString("N");
    private static WhatsAppFlowProjectionField Supplier => new(SupplierId, Key(SupplierId), "shortText", true, 40, []);
    private static WhatsAppFlowProjectionField Group(int minimum, int maximum) =>
        new(GroupId, Key(GroupId), "group", minimum > 0, null, [], Rows: new(minimum, maximum));
    private static WhatsAppFlowProjectionField Item(bool catalog = false) =>
        new(ItemId, Key(ItemId), "choice", true, null, catalog ? [] : [First, Second], Repeated: true);
    private static WhatsAppFlowProjectionField Quantity => new(QuantityId, Key(QuantityId), "decimal", true, null, [], Repeated: true);
    private static WhatsAppFlowProjectionField Note => new(NoteId, Key(NoteId), "shortText", false, 80, [], Repeated: true);
    private static IReadOnlyList<WhatsAppFlowProjectionField> Mapping(int minimum = 1, int maximum = 3, bool catalog = false) =>
        [Supplier, Group(minimum, maximum), Item(catalog), Quantity, Note];
    private static IReadOnlyList<StructuredFormCatalogOffer> Offer =>
        [new(ItemId, Key(ItemId), "Artículo", CatalogId, 2, "Manual", "Artículos", [new(First, "Primero", null)])];

    private static Dictionary<string, string> Answers(params (string Key, string Value)[] pairs)
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal) { [Key(SupplierId)] = "Proveedor" };
        foreach (var (key, value) in pairs) answers[key] = value;
        return answers;
    }
    private static (string, string)[] Row(int row, Guid item, string quantity, string? note = null) => note is null
        ? [(Key(ItemId) + "_r" + row, item.ToString("D")), (Key(QuantityId) + "_r" + row, quantity)]
        : [(Key(ItemId) + "_r" + row, item.ToString("D")), (Key(QuantityId) + "_r" + row, quantity), (Key(NoteId) + "_r" + row, note)];
    private static (string, string) More(int row, string answer) => (Key(GroupId) + "_r" + row, answer);
    private static List<Dictionary<string, string>> Items(string? json) =>
        JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json!)!;
    private static string Decoded(IReadOnlyList<WhatsAppFlowProjectionField> mapping, Dictionary<string, string> answers,
        IReadOnlyList<StructuredFormCatalogOffer>? offers = null)
    {
        var result = WhatsAppFlowRowsCodec.Decode(mapping, answers, offers ?? []);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public void One_item_answered_with_no_keeps_one_item_with_the_canonical_values_of_its_questions()
    {
        var json = Decoded(Mapping(), Answers([.. Row(1, Second, "200,50", "Sin daños"), More(1, "false")]));

        var item = Assert.Single(Items(json));
        Assert.Equal([Key(ItemId), Key(QuantityId), Key(NoteId)], item.Keys);
        Assert.Equal(Second.ToString("D"), item[Key(ItemId)]);
        Assert.Equal("200.5", item[Key(QuantityId)]);
        Assert.Equal("Sin daños", item[Key(NoteId)]);
    }

    [Fact]
    public void Two_yes_answers_and_the_last_item_without_a_question_keep_three_items()
    {
        var json = Decoded(Mapping(maximum: 3), Answers([.. Row(1, First, "1"), More(1, "true"), .. Row(2, Second, "2"), More(2, "true"),
            .. Row(3, First, "3")]));

        Assert.Equal(["1", "2", "3"], Items(json).Select(item => item[Key(QuantityId)]));
    }

    [Fact]
    public void A_block_with_minimum_zero_answered_with_no_has_no_items() =>
        Assert.Equal("[]", Decoded(Mapping(minimum: 0), Answers(More(0, "false"))));

    [Fact]
    public void A_block_with_minimum_zero_answered_with_yes_loads_its_first_item()
    {
        var json = Decoded(Mapping(minimum: 0), Answers([More(0, "true"), .. Row(1, First, "4"), More(1, "false")]));

        Assert.Equal("4", Assert.Single(Items(json))[Key(QuantityId)]);
    }

    [Fact]
    public void Items_before_the_minimum_continue_without_asking()
    {
        var json = Decoded(Mapping(minimum: 2), Answers([.. Row(1, First, "1"), .. Row(2, Second, "2"), More(2, "false")]));

        Assert.Equal(2, Items(json).Count);
    }

    [Fact]
    public void What_arrives_from_items_after_a_no_is_ignored()
    {
        var json = Decoded(Mapping(), Answers([.. Row(1, First, "1"), More(1, "false"), .. Row(2, Second, "2"), More(2, "false")]));

        Assert.Equal("1", Assert.Single(Items(json))[Key(QuantityId)]);
    }

    public static TheoryData<string> Invalid =>
    [
        "required-missing", "decision-missing", "decision-invalid", "start-missing", "asks-before-minimum", "asks-on-last",
        "outside-suffix", "question-without-suffix", "block-without-suffix", "question-in-row-zero", "unoffered",
    ];

    [Theory]
    [MemberData(nameof(Invalid))]
    public void What_the_flow_cannot_send_is_an_invalid_value(string invalid)
    {
        var (mapping, answers) = invalid switch
        {
            "required-missing" => (Mapping(), Answers([(Key(ItemId) + "_r1", First.ToString("D")), More(1, "false")])),
            "decision-missing" => (Mapping(), Answers(Row(1, First, "1"))),
            "decision-invalid" => (Mapping(), Answers([.. Row(1, First, "1"), More(1, "yes")])),
            "start-missing" => (Mapping(minimum: 0), Answers([.. Row(1, First, "1"), More(1, "false")])),
            "asks-before-minimum" => (Mapping(minimum: 2), Answers([.. Row(1, First, "1"), More(1, "true"), .. Row(2, First, "2"), More(2, "false")])),
            "asks-on-last" => (Mapping(maximum: 2), Answers([.. Row(1, First, "1"), More(1, "true"), .. Row(2, First, "2"), More(2, "false")])),
            "outside-suffix" => (Mapping(), Answers([.. Row(1, First, "1"), More(1, "false"), (Key(SupplierId) + "_r1", "Otro")])),
            "question-without-suffix" => (Mapping(), Answers([.. Row(1, First, "1"), More(1, "false"), (Key(QuantityId), "1")])),
            "block-without-suffix" => (Mapping(), Answers([.. Row(1, First, "1"), More(1, "false"), (Key(GroupId), "true")])),
            "question-in-row-zero" => (Mapping(minimum: 0), Answers([More(0, "false"), (Key(QuantityId) + "_r0", "1")])),
            _ => (Mapping(), Answers([.. Row(1, Guid.Parse("00000000-0000-7000-8000-000000000009"), "1"), More(1, "false")])),
        };

        Assert.True(WhatsAppFlowRowsCodec.Decode(mapping, answers, []).IsFailure);
    }

    [Fact]
    public void A_catalog_question_of_the_block_takes_the_offer_of_the_session()
    {
        var mapping = Mapping(catalog: true);

        var json = Decoded(mapping, Answers([.. Row(1, First, "1"), More(1, "false")]), Offer);

        Assert.Equal(First.ToString("D"), Assert.Single(Items(json))[Key(ItemId)]);
        Assert.True(WhatsAppFlowRowsCodec.Decode(mapping, Answers([.. Row(1, Second, "1"), More(1, "false")]), Offer).IsFailure);
    }

    [Fact]
    public void A_form_without_a_block_has_nothing_to_decode()
    {
        var result = WhatsAppFlowRowsCodec.Decode([Supplier], Answers(), []);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void The_keys_of_the_items_never_reach_the_owner_as_loose_answers()
    {
        var mapping = Mapping();

        Assert.True(WhatsAppFlowRowsCodec.IsRowKey(mapping, Key(QuantityId) + "_r2"));
        Assert.True(WhatsAppFlowRowsCodec.IsRowKey(mapping, Key(GroupId) + "_r1"));
        Assert.False(WhatsAppFlowRowsCodec.IsRowKey(mapping, Key(SupplierId)));
        Assert.False(WhatsAppFlowRowsCodec.IsRowKey(mapping, "field_unknown"));
    }

    public static TheoryData<string, bool> Shapes => new()
    {
        { "valid", true }, { "without-block", true }, { "two-blocks", false }, { "repeated-without-block", false },
        { "block-without-questions", false }, { "unsupported-question", false }, { "minimum-above-maximum", false },
        { "maximum-zero", false }, { "maximum-above-limit", false }, { "gap-in-block", false },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void A_projection_holds_at_most_one_block_followed_by_its_questions(string shape, bool wellFormed)
    {
        var other = Guid.Parse("00000000-0000-7000-8000-0000000000b9");
        IReadOnlyList<WhatsAppFlowProjectionField> mapping = shape switch
        {
            "valid" => Mapping(),
            "without-block" => [Supplier],
            "two-blocks" => [.. Mapping(), new(other, Key(other), "group", false, null, [], Rows: new(0, 2)), Quantity with { FieldId = SupplierId }],
            "repeated-without-block" => [Supplier, Quantity],
            "block-without-questions" => [Group(1, 3), Supplier],
            "unsupported-question" => [Group(1, 3), Quantity with { Type = "photo" }],
            "minimum-above-maximum" => [Group(4, 3), Quantity],
            "maximum-zero" => [Group(0, 0), Quantity],
            "maximum-above-limit" => [Group(1, 101), Quantity],
            _ => [Group(1, 3), Quantity, Supplier, Note],
        };

        Assert.Equal(wellFormed, WhatsAppFlowRowsCodec.IsWellFormed(mapping));
    }
}
