using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

/// <summary>
/// The references to the options chosen, built by one piece for the capture and for «¿La emito?» before «Enviar», so the
/// invoice confirmed is the one issued.
/// </summary>
public sealed class FormRecordReferencePolicyTests
{
    private static readonly Guid ClientId = Guid.Parse("00000000-0000-7000-8000-0000000000b1");
    private static readonly Guid GroupId = Guid.Parse("00000000-0000-7000-8000-0000000000b2");
    private static readonly Guid ProductId = Guid.Parse("00000000-0000-7000-8000-0000000000b3");
    private static readonly Guid QuantityId = Guid.Parse("00000000-0000-7000-8000-0000000000b4");
    private static readonly Guid SellersId = Guid.Parse("00000000-0000-7000-8000-0000000000b5");
    private static readonly Guid KindId = Guid.Parse("00000000-0000-7000-8000-0000000000b6");
    private static readonly Guid Clients = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    private static readonly Guid Products = Guid.Parse("00000000-0000-7000-8000-0000000000c2");
    private static readonly Guid People = Guid.Parse("00000000-0000-7000-8000-0000000000c3");
    private static readonly Guid Client = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid Yerba = Guid.Parse("00000000-0000-7000-8000-000000000002");
    private static readonly Guid Sugar = Guid.Parse("00000000-0000-7000-8000-000000000003");
    private static readonly Guid Ana = Guid.Parse("00000000-0000-7000-8000-000000000004");
    private static readonly Guid Beto = Guid.Parse("00000000-0000-7000-8000-000000000005");
    private static readonly Guid Inline = Guid.Parse("00000000-0000-7000-8000-000000000006");
    private static readonly Guid RecordId = Guid.Parse("00000000-0000-7000-8000-0000000000d1");
    private const string ClientFields = """[{"key":"cuit","value":"20123456786"}]""";

    private static CanonicalFormField Field(Guid id, string type, Guid? catalog = null, bool? repeated = null, FormRowSettings? rows = null) =>
        new(id, $"field_{id:N}", type, new Dictionary<string, string> { ["es"] = "Pregunta" }, null, false, null,
            type == "choice" && catalog is null ? [new(Inline, new Dictionary<string, string> { ["es"] = "Contado" })] : null,
            catalog is { } value ? new FormQuestionSource(value) : null, Rows: rows, Repeated: repeated);

    private static readonly FormVersionSchema Schema = new(1,
    [
        Field(ClientId, "choice", Clients),
        Field(GroupId, "group", rows: new(0, 3)),
        Field(ProductId, "choice", Products, repeated: true),
        Field(QuantityId, "decimal", repeated: true),
        Field(SellersId, "multiChoice", People),
        Field(KindId, "choice"),
    ]);

    private static readonly IReadOnlyList<StructuredFormCatalogOffer> Offers =
    [
        new(ClientId, $"field_{ClientId:N}", "Cliente", Clients, 4, "Manual", "Clientes",
            [new(Client, "Juana Perez", "CUIT 20-12345678-6",
                new Dictionary<string, StructuredFormCatalogOptionText> { ["en"] = new("Juana Perez (en)", null) }, "20123456786")],
            new Dictionary<string, string> { ["en"] = "Clients" }, "Integer"),
        new(ProductId, $"field_{ProductId:N}", "Producto", Products, 2, "External", "Productos",
            [new(Yerba, "Yerba", null), new(Sugar, "Azucar", "1 kg")]),
        new(SellersId, $"field_{SellersId:N}", "Vendedores", People, 7, "Filters", "Personas",
            [new(Ana, "Ana", null), new(Beto, "Beto", null)]),
    ];

    private static FormRecordAnswer Choice(Guid fieldId, Guid choiceId) => new(fieldId, "choice", null, null, null, null, choiceId);

    // In the order of the questions: a manual client, two items of the block with an external product each, two people in the
    // order chosen and an option of the form itself, which has no catalog.
    private static readonly IReadOnlyList<FormRecordAnswer> Answers =
    [
        Choice(ClientId, Client),
        new(GroupId, "group", null, null, null, null, null)
        {
            Rows =
            [
                [Choice(ProductId, Yerba), new(QuantityId, "decimal", null, 2m, null, null, null)],
                [Choice(ProductId, Sugar), new(QuantityId, "decimal", null, 1m, null, null, null)],
            ],
        },
        new(SellersId, "multiChoice", null, null, null, null, null, [Beto, Ana]),
        Choice(KindId, Inline),
    ];

    [Fact]
    public void Each_option_chosen_is_a_reference_with_its_text_value_translations_and_item_and_only_a_manual_one_keeps_its_fields()
    {
        var chosen = FormRecordReferencePolicy.Chosen(Schema, Answers, Offers);
        var fields = new Dictionary<Guid, string> { [Client] = ClientFields, [Yerba] = """[{"key":"origin","value":"AR"}]""" };

        var references = FormRecordReferencePolicy.References(RecordId, chosen, fields);

        FormRecordReferenceRow[] expected =
        [
            new(RecordId, ClientId, Clients, 4, Client, "Juana Perez", "CUIT 20-12345678-6", "Clientes",
                """{"en":{"text":"Juana Perez (en)","detail":null,"catalogName":"Clients"}}""", "20123456786", "Integer", ClientFields),
            new(RecordId, ProductId, Products, 2, Yerba, "Yerba", null, "Productos"),
            new(RecordId, ProductId, Products, 2, Sugar, "Azucar", "1 kg", "Productos", Row: 1),
            new(RecordId, SellersId, People, 7, Beto, "Beto", null, "Personas"),
            new(RecordId, SellersId, People, 7, Ana, "Ana", null, "Personas"),
        ];
        Assert.Equal(expected, references);
    }

    [Fact]
    public void Only_the_options_of_a_catalog_with_items_have_their_fields_read_each_once()
    {
        var answers = Answers.Select(answer => answer.FieldId == GroupId ? answer with
        {
            Rows = [[Choice(ProductId, Yerba)], [Choice(ProductId, Yerba)], [Choice(ProductId, Sugar)]],
        } : answer).ToArray();

        var itemIds = FormRecordReferencePolicy.ItemIds(FormRecordReferencePolicy.Chosen(Schema, answers, Offers));

        Assert.Equal([Client, Yerba, Sugar], itemIds);
    }

    [Fact]
    public void Each_answer_is_placed_in_its_item_of_the_block_and_the_block_itself_is_not_a_value()
    {
        var placed = FormRecordReferencePolicy.Placed(Answers).ToArray();

        Assert.Equal([ClientId, ProductId, QuantityId, ProductId, QuantityId, SellersId, KindId], placed.Select(value => value.Answer.FieldId));
        Assert.Equal(new int?[] { null, 0, 0, 1, 1, null, null }, placed.Select(value => value.Item));
    }
}
