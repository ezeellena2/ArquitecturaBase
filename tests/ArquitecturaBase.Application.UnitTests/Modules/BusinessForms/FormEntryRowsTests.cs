using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormEntryRowsTests
{
    private static readonly Guid SupplierId = Guid.Parse("00000000-0000-7000-8000-0000000000d0");
    private static readonly Guid GroupId = Guid.Parse("00000000-0000-7000-8000-0000000000d1");
    private static readonly Guid QuantityId = Guid.Parse("00000000-0000-7000-8000-0000000000d2");
    private static readonly Guid UnitId = Guid.Parse("00000000-0000-7000-8000-0000000000d3");
    private static readonly Guid NoteId = Guid.Parse("00000000-0000-7000-8000-0000000000d4");
    private static readonly Guid Liter = Guid.Parse("00000000-0000-7000-8000-0000000000f1");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key(Guid id) => "field_" + id.ToString("N");
    private static CanonicalFormField Field(Guid id, string type, bool required = true, FormRowSettings? rows = null, bool? repeated = null,
        FormQuestionSource? source = null) => new(id, Key(id), type, FormCatalogPreparationFixture.Texts, null, required,
            type == "shortText" ? 100 : null, type == "choice" && source is null ? [new(Liter, FormCatalogPreparationFixture.Texts)] : null, source,
            Rows: rows, Repeated: repeated);
    private static IReadOnlyList<CanonicalFormField> Block(params CanonicalFormField[] extra) =>
        [Field(SupplierId, "shortText"), Field(GroupId, "group", rows: new(1, 3)), Field(QuantityId, "decimal", repeated: true),
            Field(UnitId, "choice", repeated: true), .. extra];
    private static string Schema(IReadOnlyList<CanonicalFormField> questions) =>
        JsonSerializer.Serialize(new FormVersionSchema(1, questions), JsonSerializerOptions.Web);
    private static FormEntryAnswer Item(Guid fieldId, string value) => new(fieldId, value, null);
    private static FormEntryAnswer Rows(params FormEntryAnswer[][] items) => new(GroupId, null, null, items);
    private static FormEntryFixture Fixture(IReadOnlyList<CanonicalFormField>? questions = null)
    {
        var fixture = new FormEntryFixture();
        fixture.SchemaJson = Schema(questions ?? Block());
        return fixture;
    }

    [Fact]
    public async Task The_entry_describes_the_block_with_its_rows_and_its_questions_as_repeated()
    {
        using var fixture = Fixture();

        var entry = await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct);

        Assert.True(entry.IsSuccess);
        var questions = entry.Value.Questions;
        Assert.Equal([SupplierId, GroupId, QuantityId, UnitId], questions.Select(question => question.FieldId));
        Assert.Equal(new FormRowSettings(1, 3), questions[1].Rows);
        Assert.Equal(new bool?[] { null, null, true, true }, questions.Select(question => question.Repeated));
        Assert.Null(questions[0].Rows);
    }

    [Fact]
    public async Task A_send_with_items_reaches_the_capture_under_the_key_of_the_block_in_the_order_of_its_questions()
    {
        using var fixture = Fixture();

        var sent = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(Item(SupplierId, "Proveedor"),
            Rows([Item(UnitId, Liter.ToString("D")), Item(QuantityId, "200.5")], [Item(QuantityId, "100"), Item(UnitId, "")])), Ct);

        Assert.True(sent.IsSuccess);
        var answers = Assert.Single(fixture.Captures).Answers;
        Assert.Equal("Proveedor", answers[Key(SupplierId)]);
        Assert.Equal($$"""[{"{{Key(QuantityId)}}":"200.5","{{Key(UnitId)}}":"{{Liter:D}}"},{"{{Key(QuantityId)}}":"100"}]""", answers[Key(GroupId)]);
        Assert.Equal(2, answers.Count);
    }

    public static TheoryData<string> Misplaced => ["rows-outside-the-block", "block-question-outside", "foreign-question-in-item", "block-without-rows"];

    [Theory]
    [MemberData(nameof(Misplaced))]
    public async Task Answers_of_an_item_go_only_inside_the_block(string misplaced)
    {
        using var fixture = Fixture();
        FormEntryAnswer[] answers = misplaced switch
        {
            "rows-outside-the-block" => [new(SupplierId, null, null, [[Item(QuantityId, "1")]])],
            "block-question-outside" => [Item(QuantityId, "1")],
            "foreign-question-in-item" => [Rows([Item(SupplierId, "Otro")])],
            _ => [Item(GroupId, "[]")],
        };

        var sent = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(answers), Ct);

        Assert.Equal(FormRecordErrors.InvalidResponse.Code, sent.Error.Code);
        Assert.Empty(fixture.Captures);
    }

    [Fact]
    public void A_send_takes_up_to_a_hundred_items_of_up_to_forty_answers_without_nesting()
    {
        FormEntryAnswer[][] Items(int count, int answers) =>
            [.. Enumerable.Range(0, count).Select(_ => Enumerable.Range(0, answers).Select(_ => Item(Guid.NewGuid(), "1")).ToArray())];

        Assert.True(FormEntrySendRules.ValidAnswers([new(GroupId, null, null, Items(100, 40))]));
        Assert.False(FormEntrySendRules.ValidAnswers([new(GroupId, null, null, Items(101, 1))]));
        Assert.False(FormEntrySendRules.ValidAnswers([new(GroupId, null, null, Items(1, 41))]));
        Assert.False(FormEntrySendRules.ValidAnswers([new(GroupId, null, null, [[new(QuantityId, null, null, [[Item(UnitId, "1")]])]])]));
        Assert.False(FormEntrySendRules.ValidAnswers([new(GroupId, null, null, [[Item(QuantityId, "1"), Item(QuantityId, "2")]])]));
        Assert.False(FormEntrySendRules.ValidAnswers([new(GroupId, null, null, [[new(UnitId, null, [Liter])]])]));
        Assert.False(FormEntrySendRules.ValidAnswers([new(GroupId, "x", null, [[Item(QuantityId, "1")]])]));
    }

    [Fact]
    public async Task A_dependent_list_of_the_block_uses_the_answer_of_its_own_item()
    {
        using var fixture = new FormEntryFixture(dependsOnAnswer: true);
        var answer = fixture.Catalogs.AnswerFieldId;
        var catalog = fixture.Catalogs.Catalog.Id;
        fixture.SchemaJson = Schema([Field(GroupId, "group", rows: new(1, 3)), Field(answer, "choice", false, repeated: true, source: new(catalog)),
            Field(fixture.FieldId, "choice", repeated: true, source: new(catalog, [new(fixture.Catalogs.FilterFieldId, QuestionId: answer)]))]);
        var ombu = fixture.AddItem("El Ombú", 1);
        var ceibo = fixture.AddItem("El Ceibo", 2);
        var north = fixture.AddItem("Lote norte", 3, answer: ombu);
        var south = fixture.AddItem("Lote sur", 4, answer: ceibo);

        var mismatched = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(Rows(
            [Item(answer, ombu.Id.ToString("D")), Item(fixture.FieldId, north.Id.ToString("D"))],
            [Item(answer, ceibo.Id.ToString("D")), Item(fixture.FieldId, north.Id.ToString("D"))])), Ct);
        Assert.Empty(fixture.Captures);
        var sent = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(Rows(
            [Item(answer, ombu.Id.ToString("D")), Item(fixture.FieldId, north.Id.ToString("D"))],
            [Item(answer, ceibo.Id.ToString("D")), Item(fixture.FieldId, south.Id.ToString("D"))])), Ct);

        var error = Assert.IsType<ValidationError>(mismatched.Error);
        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, error.Code);
        Assert.Equal([$"answers.{GroupId:D}.1.{fixture.FieldId:D}"], error.Errors.Keys);
        Assert.True(sent.IsSuccess);
        var offers = Assert.Single(fixture.Captures).Catalogs!;
        Assert.Equal(new[] { north.Id, south.Id }.Order(), Assert.Single(offers, offer => offer.FieldId == fixture.FieldId).Options.Select(option => option.Id).Order());
        Assert.Equal(new[] { ombu.Id, ceibo.Id }.Order(), Assert.Single(offers, offer => offer.FieldId == answer).Options.Select(option => option.Id).Order());
    }

    [Fact]
    public async Task Items_read_a_catalog_once_per_question_or_once_per_answer_of_their_own_item_it_depends_on()
    {
        using var fixture = new FormEntryFixture(dependsOnAnswer: true);
        var answer = fixture.Catalogs.AnswerFieldId;
        var catalog = fixture.Catalogs.Catalog.Id;
        fixture.SchemaJson = Schema([Field(GroupId, "group", rows: new(1, 3)), Field(answer, "choice", false, repeated: true, source: new(catalog)),
            Field(fixture.FieldId, "choice", repeated: true, source: new(catalog, [new(fixture.Catalogs.FilterFieldId, QuestionId: answer)]))]);
        var ombu = fixture.AddItem("El Ombú", 1);
        var ceibo = fixture.AddItem("El Ceibo", 2);
        var north = fixture.AddItem("Lote norte", 3, answer: ombu);
        var east = fixture.AddItem("Lote este", 4, answer: ombu);
        var south = fixture.AddItem("Lote sur", 5, answer: ceibo);

        var sent = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(Rows(
            [Item(answer, ombu.Id.ToString("D")), Item(fixture.FieldId, north.Id.ToString("D"))],
            [Item(answer, ombu.Id.ToString("D")), Item(fixture.FieldId, east.Id.ToString("D"))],
            [Item(answer, ceibo.Id.ToString("D")), Item(fixture.FieldId, south.Id.ToString("D"))])), Ct);

        Assert.True(sent.IsSuccess);
        // The list without a filter is read once with every option chosen; the dependent one, once per answer of its items.
        Assert.Single(fixture.ItemQueries, query => query.Containment is null);
        Assert.Equal(2, fixture.ItemQueries.Count(query => query.Containment is not null));
        var offer = Assert.Single(Assert.Single(fixture.Captures).Catalogs!, offer => offer.FieldId == fixture.FieldId);
        Assert.Equal(new[] { north.Id, east.Id, south.Id }.Order(), offer.Options.Select(option => option.Id).Order());
    }

    [Fact]
    public async Task A_list_of_the_block_that_depends_on_a_question_outside_takes_that_answer_for_every_item()
    {
        using var fixture = new FormEntryFixture(dependsOnAnswer: true);
        var answer = fixture.Catalogs.AnswerFieldId;
        var catalog = fixture.Catalogs.Catalog.Id;
        fixture.SchemaJson = Schema([Field(answer, "choice", false, source: new(catalog)), Field(GroupId, "group", rows: new(1, 3)),
            Field(fixture.FieldId, "choice", repeated: true, source: new(catalog, [new(fixture.Catalogs.FilterFieldId, QuestionId: answer)]))]);
        var ombu = fixture.AddItem("El Ombú", 1);
        var ceibo = fixture.AddItem("El Ceibo", 2);
        var north = fixture.AddItem("Lote norte", 3, answer: ombu);
        var south = fixture.AddItem("Lote sur", 4, answer: ceibo);

        var outside = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(Item(answer, ombu.Id.ToString("D")),
            Rows([Item(fixture.FieldId, north.Id.ToString("D"))], [Item(fixture.FieldId, south.Id.ToString("D"))])), Ct);
        var sent = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(Item(answer, ombu.Id.ToString("D")),
            Rows([Item(fixture.FieldId, north.Id.ToString("D"))], [Item(fixture.FieldId, north.Id.ToString("D"))])), Ct);

        Assert.Equal([$"answers.{GroupId:D}.1.{fixture.FieldId:D}"], Assert.IsType<ValidationError>(outside.Error).Errors.Keys);
        Assert.True(sent.IsSuccess);
        Assert.Equal(north.Id, Assert.Single(Assert.Single(Assert.Single(fixture.Captures).Catalogs!, offer => offer.FieldId == fixture.FieldId).Options).Id);
    }

    [Fact]
    public async Task An_option_retired_in_an_item_names_the_cell_of_that_item()
    {
        using var fixture = Fixture();
        fixture.Captured = new("Rejected", null, null, FormRecordErrors.CatalogItemInactive.Code, UnitId, 2);

        var sent = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(Item(SupplierId, "Proveedor"),
            Rows([Item(QuantityId, "1")], [Item(QuantityId, "2")], [Item(QuantityId, "3"), Item(UnitId, Liter.ToString("D"))])), Ct);

        var error = Assert.IsType<ValidationError>(sent.Error);
        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, error.Code);
        Assert.Equal([$"answers.{GroupId:D}.2.{UnitId:D}"], error.Errors.Keys);
    }

    [Fact]
    public async Task A_correction_opens_the_items_with_the_questions_that_remain_in_the_block()
    {
        using var fixture = Fixture();
        var recordSchema = Schema(Block(Field(NoteId, "shortText", false, repeated: true)));
        var values = JsonSerializer.Serialize(new object[]
        {
            new { fieldId = SupplierId, value = "Proveedor" },
            new
            {
                fieldId = GroupId, value = new object[]
                {
                    new object[] { new { fieldId = QuantityId, value = 200.5m }, new { fieldId = UnitId, value = Liter }, new { fieldId = NoteId, value = "Sin daños" } },
                    new object[] { new { fieldId = QuantityId, value = 100m } },
                },
            },
        }, JsonSerializerOptions.Web);
        var recordId = Guid.NewGuid();
        fixture.Responses.ExportRows = [new(new(Guid.NewGuid(), recordId, 7, fixture.Catalogs.DefinitionId, "SIE", Guid.NewGuid(), 1,
            """{"es":"Recepción"}""", recordSchema, values, []), new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc), new(fixture.Actor, "Autor"))];

        var correction = await fixture.Service.GetCorrectionAsync(fixture.Catalogs.DefinitionId, recordId, Ct);
        fixture.SchemaJson = Schema([Field(SupplierId, "shortText"), Field(QuantityId, "decimal")]);
        var withoutBlock = await fixture.Service.GetCorrectionAsync(fixture.Catalogs.DefinitionId, recordId, Ct);

        Assert.True(correction.IsSuccess);
        var block = Assert.Single(correction.Value.Answers, answer => answer.FieldId == GroupId);
        Assert.Null(block.Value);
        var rows = block.Rows!;
        Assert.Equal(2, rows.Count);
        Assert.Equal([QuantityId, UnitId], rows[0].Select(answer => answer.FieldId));
        Assert.Equal(["200.5", Liter.ToString("D")], rows[0].Select(answer => answer.Value));
        Assert.Equal("100", Assert.Single(rows[1]).Value);
        Assert.Equal(["Proveedor"], correction.Value.Answers.Where(answer => answer.FieldId != GroupId).Select(answer => answer.Value));
        Assert.True(withoutBlock.IsSuccess);
        Assert.DoesNotContain(withoutBlock.Value.Answers, answer => answer.FieldId == GroupId || answer.Rows is not null);
    }
}
