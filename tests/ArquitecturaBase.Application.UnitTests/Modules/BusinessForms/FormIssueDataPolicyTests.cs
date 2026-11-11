using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormIssueDataPolicyTests
{
    private static readonly Guid CustomerId = Guid.NewGuid(), CatalogId = Guid.NewGuid(), ItemId = Guid.NewGuid(), ConditionId = Guid.NewGuid();

    [Fact]
    public void A_catalog_option_and_a_captured_field_that_names_another_option_go_by_their_codes()
    {
        var row = Row();

        Assert.Equal([ItemId, ConditionId], FormIssueDataPolicy.ReferencedItemIds(row));
        var datum = Assert.Single(FormIssueDataPolicy.Read(row, new Dictionary<string, Guid> { ["customer"] = CustomerId }, "es",
            new Dictionary<Guid, string> { [ItemId] = "c-17", [ConditionId] = "6" }));

        Assert.Equal(("customer", "c-17", "Juana Pérez"), (datum.Key, datum.Value, datum.Label));
        Assert.Equal(("20123456786", "6"), (datum.Fields!["tax_id"], datum.Fields["vat_condition"]));
    }

    [Fact]
    public void Without_a_code_an_option_keeps_its_id()
    {
        var datum = Assert.Single(FormIssueDataPolicy.Read(Row(), new Dictionary<string, Guid> { ["customer"] = CustomerId }, "es", new Dictionary<Guid, string>()));

        Assert.Equal((ItemId.ToString("D"), ConditionId.ToString("D")), (datum.Value, datum.Fields!["vat_condition"]));
    }

    private static FormRecordStoredRow Row()
    {
        var question = new CanonicalFormField(CustomerId, "customer", "choice", new Dictionary<string, string> { ["es"] = "Cliente" }, null, true, null, null,
            Source: new FormQuestionSource(CatalogId));
        var fields = JsonSerializer.Serialize(new[]
        {
            new FormCatalogCapturedField(Guid.NewGuid(), "tax_id", new Dictionary<string, string> { ["es"] = "CUIT" }, "text", "data", JsonSerializer.SerializeToElement("20123456786")),
            new FormCatalogCapturedField(Guid.NewGuid(), "vat_condition", new Dictionary<string, string> { ["es"] = "Condición" }, "catalog", "data",
                JsonSerializer.SerializeToElement(ConditionId.ToString("D"))),
        }, JsonSerializerOptions.Web);
        var recordId = Guid.NewGuid();
        var reference = new FormRecordReferenceRow(recordId, CustomerId, CatalogId, 3, ItemId, "Juana Pérez", null, "Clientes", FieldValuesJson: fields);
        return new FormRecordStoredRow(Guid.NewGuid(), recordId, 1, Guid.NewGuid(), "FAC", Guid.NewGuid(), 1, "{\"es\":\"Factura\"}",
            JsonSerializer.Serialize(new FormVersionSchema(1, [question]), JsonSerializerOptions.Web),
            JsonSerializer.Serialize(new[] { new { fieldId = CustomerId, value = ItemId } }), [reference]);
    }
}
