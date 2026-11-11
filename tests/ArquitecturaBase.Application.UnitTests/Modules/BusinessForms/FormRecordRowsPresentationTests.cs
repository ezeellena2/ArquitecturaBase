using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormRecordRowsPresentationTests
{
    private static readonly Guid RecordId = Guid.Parse("00000000-0000-7000-8000-0000000000b1");
    private static readonly Guid SupplierId = Guid.Parse("00000000-0000-7000-8000-0000000000a0");
    private static readonly Guid GroupId = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
    private static readonly Guid ItemId = Guid.Parse("00000000-0000-7000-8000-0000000000a2");
    private static readonly Guid QuantityId = Guid.Parse("00000000-0000-7000-8000-0000000000a3");
    private static readonly Guid NoteId = Guid.Parse("00000000-0000-7000-8000-0000000000a4");
    private static readonly Guid CatalogId = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    private static readonly Guid First = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-7000-8000-000000000002");

    private static CanonicalFormField Field(Guid id, string type, string label, bool required = true, FormRowSettings? rows = null,
        bool? repeated = null, Guid? catalog = null) => new(id, $"field_{id:N}", type,
            new Dictionary<string, string> { ["es"] = label, ["en"] = label + " (en)" }, null, required, type == "shortText" ? 100 : null,
            null, catalog is { } value ? new FormQuestionSource(value) : null, Rows: rows, Repeated: repeated);
    private static FormVersionSchema Schema => new(1,
    [
        Field(SupplierId, "shortText", "Proveedor"),
        Field(GroupId, "group", "Artículos", false, new(0, 3)),
        Field(ItemId, "choice", "Artículo", repeated: true, catalog: CatalogId),
        Field(QuantityId, "decimal", "Cantidad", repeated: true),
        Field(NoteId, "shortText", "Observación", false, repeated: true),
    ]);
    private static FormRecordReferenceRow Reference(Guid item, string text, int row) =>
        new(RecordId, ItemId, CatalogId, 3, item, text, null, "Artículos", Row: row);
    private static FormRecordStoredRow Stored(object? group, params FormRecordReferenceRow[] references)
    {
        var values = new List<object> { new { fieldId = SupplierId, value = "Proveedor del norte" } };
        if (group is not null) values.Add(new { fieldId = GroupId, value = group });
        return new(Guid.NewGuid(), RecordId, 7, Guid.NewGuid(), "REC", Guid.NewGuid(), 2, """{"es":"Recepción","en":"Reception"}""",
            JsonSerializer.Serialize(Schema, JsonSerializerOptions.Web), JsonSerializer.Serialize(values, JsonSerializerOptions.Web), references);
    }
    private static object[] Item(Guid item, decimal quantity, string? note = null) => note is null
        ? new object[] { new { fieldId = ItemId, value = item }, new { fieldId = QuantityId, value = quantity } }
        : new object[] { new { fieldId = ItemId, value = item }, new { fieldId = QuantityId, value = quantity }, new { fieldId = NoteId, value = note } };

    private static readonly Guid GoodId = Guid.Parse("00000000-0000-7000-8000-0000000000a5");
    /// <summary>The block of the messages: a choice of its own, the quantity, whether it arrived in good condition and a note.</summary>
    private static FormVersionSchema MessageSchema => new(1,
    [
        Field(SupplierId, "shortText", "Proveedor"),
        Field(GroupId, "group", "Artículos", false, new(0, 3)),
        Field(ItemId, "choice", "Artículo", repeated: true) with
        {
            Options = [new(First, new Dictionary<string, string> { ["es"] = "Semilla", ["en"] = "Seed" }),
                new(Second, new Dictionary<string, string> { ["es"] = "Fertilizante", ["en"] = "Fertilizer" })],
        },
        Field(QuantityId, "decimal", "Cantidad", repeated: true),
        Field(GoodId, "boolean", "¿Llegó bien?", repeated: true),
        Field(NoteId, "shortText", "Observación", false, repeated: true),
    ]);
    private static object[] Arrived(Guid item, decimal quantity, bool good, string? note = null) => note is null
        ? new object[] { new { fieldId = ItemId, value = item }, new { fieldId = QuantityId, value = quantity }, new { fieldId = GoodId, value = good } }
        : new object[] { new { fieldId = ItemId, value = item }, new { fieldId = QuantityId, value = quantity }, new { fieldId = GoodId, value = good }, new { fieldId = NoteId, value = note } };
    private static string Message(string culture, string template, params object[][] items) => Message(culture, template, MessageSchema, items);
    /// <summary>The text of a notice with the template in the culture, for a response of the schema with these items (no entry with null).</summary>
    private static string Message(string culture, string template, FormVersionSchema schema, object[][]? items)
    {
        var stored = Stored(items) with { SchemaJson = JsonSerializer.Serialize(schema, JsonSerializerOptions.Web) };
        var action = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string, string> { [culture] = template }, false);
        var snapshot = new FormActionRecordRow(new(stored, new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc), new(Guid.NewGuid(), "Author")), culture);
        return FormActionMessagePolicy.Render(action, MessageSchema, snapshot, culture);
    }

    [Fact]
    public void A_response_reads_the_block_in_its_place_with_its_items_and_not_its_questions_apart()
    {
        var stored = Stored(new[] { Item(First, 200.5m, "Sin daños"), Item(First, 100m) },
            Reference(First, "Primero del ítem dos", 1), Reference(First, "Primero", 0));

        var reply = FormRecordPresentationPolicy.Project(stored, "es", Schema);

        Assert.Equal([SupplierId, GroupId], reply.Values.Select(value => value.FieldId));
        var block = reply.Values[1];
        Assert.Equal(("group", "Artículos"), (block.Type, block.Label));
        var rows = Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordReadRow>>(block.Value);
        Assert.Equal(["Primero", "Primero del ítem dos"], rows.Select(row => Assert.IsType<StructuredRecordChoiceValue>(row.Values[0].Value).Label));
        Assert.Equal(["200.5", "100"], rows.Select(row => row.Values[1].Value));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(block, JsonSerializerOptions.Web));
        Assert.Equal("Cantidad", json.RootElement.GetProperty("value")[1].GetProperty("values")[1].GetProperty("label").GetString());
    }

    [Fact]
    public void A_block_without_items_reads_an_empty_list_and_a_version_without_the_block_has_no_value_for_it()
    {
        var empty = FormRecordPresentationPolicy.Project(Stored(Array.Empty<object>()), "es", Schema);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordReadRow>>(empty.Values.Single(value => value.FieldId == GroupId).Value));

        var schema = new FormVersionSchema(1, [Schema.Questions[0]]);
        var without = FormRecordPresentationPolicy.Project(Stored(null) with { SchemaJson = JsonSerializer.Serialize(schema, JsonSerializerOptions.Web) }, "es", schema);
        Assert.Equal([SupplierId], without.Values.Select(value => value.FieldId));
    }

    [Fact]
    public void The_items_leave_out_the_catalog_fields_when_the_projection_does_not_take_them()
    {
        var fields = FormDraftPolicy.Serialize(new[] { new FormCatalogCapturedField(Guid.NewGuid(), "days",
            new Dictionary<string, string> { ["es"] = "Días" }, "integer", "data", JsonSerializer.SerializeToElement(30)) });
        var stored = Stored(new[] { Item(First, 1m) }, Reference(First, "Primero", 0) with { FieldValuesJson = fields });
        static StructuredRecordChoiceValue Choice(StructuredRecordReadReply reply) => Assert.IsType<StructuredRecordChoiceValue>(
            Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordReadRow>>(reply.Values[1].Value)[0].Values[0].Value);

        Assert.NotNull(Choice(FormRecordPresentationPolicy.Project(stored, "es", Schema)).Fields);
        Assert.Null(Choice(FormRecordPresentationPolicy.Project(stored, "es", Schema, includeCatalogFields: false)).Fields);
    }

    [Theory]
    [InlineData("es", "Llegó de Proveedor del norte: \n1. Semilla · 200,5 · Sí · Sin daños\n2. Semilla · 100 · No\n3. Fertilizante · 7 · Sí")]
    [InlineData("en", "Llegó de Proveedor del norte: \n1. Seed · 200.5 · Yes · Sin daños\n2. Seed · 100 · No\n3. Fertilizer · 7 · Yes")]
    public void A_rule_message_writes_the_block_with_one_numbered_item_per_line_and_its_answers_in_order(string culture, string expected) =>
        Assert.Equal(expected, Message(culture, $"Llegó de {{{SupplierId:D}}}: {{{GroupId:D}}}",
            Arrived(First, 200.5m, true, "Sin daños"), Arrived(First, 100m, false), Arrived(Second, 7m, true)));

    private static readonly Guid DueId = Guid.Parse("00000000-0000-7000-8000-0000000000a6");

    [Theory]
    [InlineData("es", "Llegó: \n1. Semilla · 1 · Sí · 03/11/2026")]
    [InlineData("en", "Llegó: \n1. Seed · 1 · Yes · 11/03/2026")]
    public void A_date_in_a_rule_message_goes_with_numbers_only_and_two_digits_in_the_order_of_the_language(string culture, string expected)
    {
        var schema = new FormVersionSchema(1, [.. MessageSchema.Questions, Field(DueId, "date", "Vence", false, repeated: true)]);
        Assert.Equal(expected, Message(culture, $"Llegó: {{{GroupId:D}}}", schema,
            [[.. Arrived(First, 1m, true), new { fieldId = DueId, value = "2026-11-03" }]]));
    }

    [Fact]
    public void The_first_item_starts_on_a_new_line_unless_the_message_starts_there_or_after_a_line_break()
    {
        var item = Arrived(First, 1m, true);
        Assert.Equal("1. Semilla · 1 · Sí", Message("es", $"{{{GroupId:D}}}", item));
        Assert.Equal("Llegó:\n1. Semilla · 1 · Sí", Message("es", $"Llegó:\n{{{GroupId:D}}}", item));
        Assert.Equal("Llegó: \n1. Semilla · 1 · Sí\nGracias", Message("es", $"Llegó: {{{GroupId:D}}}\nGracias", item));
    }

    [Fact]
    public void A_block_without_items_or_from_a_version_without_it_leaves_the_datum_empty_and_an_empty_item_keeps_its_number()
    {
        var template = $"Llegó: {{{GroupId:D}}}";
        Assert.Equal("Llegó: ", Message("es", template));
        Assert.Equal("Llegó: \n1. Semilla · 1 · Sí\n2.", Message("es", template, Arrived(First, 1m, true), []));
        Assert.Equal("Llegó: ", Message("es", template, new FormVersionSchema(1, [MessageSchema.Questions[0]]), null));
    }

    [Fact]
    public void Each_item_reads_every_question_of_its_block_in_order_with_the_reference_of_its_own_row()
    {
        var stored = Stored(new[] { Item(First, 200.5m, "Sin daños"), Item(First, 100m), Item(Second, 7m) },
            Reference(Second, "Segundo", 2), Reference(First, "Primero del ítem dos", 1), Reference(First, "Primero", 0));

        var rows = FormRecordPresentationPolicy.Rows(stored, "es", Schema);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal([ItemId, QuantityId, NoteId], row.Values.Select(value => value.FieldId)));
        Assert.Equal(["Artículo", "Cantidad", "Observación"], rows[0].Values.Select(value => value.Label));
        Assert.Equal(["Primero", "Primero del ítem dos", "Segundo"],
            rows.Select(row => Assert.IsType<StructuredRecordChoiceValue>(row.Values[0].Value).Label));
        Assert.Equal(["200.5", "100", "7"], rows.Select(row => row.Values[1].Value));
        Assert.Equal("Sin daños", rows[0].Values[2].Value);
        Assert.Null(rows[1].Values[2].Value);
        Assert.Equal("choice", rows[0].Values[0].Type);
    }

    [Fact]
    public void Items_read_their_labels_in_the_culture_of_the_reader()
    {
        var rows = FormRecordPresentationPolicy.Rows(Stored(new[] { Item(First, 1m) }, Reference(First, "Primero", 0)), "en", Schema);

        Assert.Equal("Cantidad (en)", Assert.Single(rows).Values[1].Label);
    }

    [Fact]
    public void A_block_without_items_or_without_an_answer_has_no_rows()
    {
        Assert.Empty(FormRecordPresentationPolicy.Rows(Stored(Array.Empty<object>()), "es", Schema));
        Assert.Empty(FormRecordPresentationPolicy.Rows(Stored(null), "es", Schema));
    }
}
