using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormResponsePresentationPolicyTests
{
    [Fact]
    public void Columns_follow_the_active_order_and_keep_removed_questions_with_their_identity()
    {
        var original = Field("decimal", "Importe", "Amount");
        var changed = Field("shortText", "Monto", "Total");
        var renamed = Field("longText", "Notas", "Notes");
        var old = Version(1, [original, renamed with { Label = Text("Observaciones", "Comments") }]);
        var active = Version(2, [renamed, changed]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", active.Id, [old, active]);
        var columns = FormResponsePresentationPolicy.Columns(definition, "en");
        Assert.Equal(new[] { renamed.FieldId, changed.FieldId, original.FieldId }, columns.Select(column => column.FieldId));
        Assert.Equal(new[] { "Notes", "Total", "Amount" }, columns.Select(column => column.Label));
        Assert.Equal(new long[] { 1, 2, 1 }, columns.Select(column => column.IntroducedInVersion));
    }
    [Fact]
    public void The_block_of_rows_has_one_column_with_its_questions_joined_across_versions()
    {
        var supplier = Field("shortText", "Proveedor", "Supplier");
        var block = Field("group", "Artículos", "Items") with { Rows = new FormRowSettings(1, 10) };
        var quantity = Field("decimal", "Cantidad", "Quantity") with { Repeated = true };
        var lot = Field("shortText", "Lote", "Lot") with { Repeated = true };
        var item = Field("choice", "Artículo", "Item") with { Repeated = true, Options = [new(Guid.NewGuid(), Text("Semilla", "Seed"))] };
        var old = Version(1, [supplier, block, quantity, lot]);
        var active = Version(2, [supplier, block, item, quantity]);

        var columns = FormResponsePresentationPolicy.Columns(new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", active.Id, [old, active]), "en");

        Assert.Equal(new[] { supplier.FieldId, block.FieldId }, columns.Select(column => column.FieldId));
        Assert.Null(columns[0].Questions);
        var group = columns[1];
        Assert.Equal(("group", "Items", 1L), (group.Type, group.Label, group.IntroducedInVersion));
        Assert.Empty(group.Options);
        Assert.Equal(new[] { item.FieldId, quantity.FieldId, lot.FieldId }, group.Questions!.Select(column => column.FieldId));
        Assert.Equal(new[] { "Item", "Quantity", "Lot" }, group.Questions!.Select(column => column.Label));
        Assert.Equal(new long[] { 2, 1, 1 }, group.Questions!.Select(column => column.IntroducedInVersion));
        Assert.Equal("Seed", Assert.Single(group.Questions![0].Options).Label);
        Assert.All(group.Questions!, column => Assert.Null(column.Questions));
    }
    [Fact]
    public void A_question_moved_into_the_block_keeps_its_old_column_as_a_retired_question()
    {
        var quantity = Field("decimal", "Cantidad", "Quantity");
        var block = Field("group", "Artículos", "Items") with { Rows = new FormRowSettings(1, 10) };
        var moved = Field("decimal", "Cantidad", "Quantity") with { Repeated = true };
        var old = Version(1, [quantity]);
        var active = Version(2, [block, moved]);

        var columns = FormResponsePresentationPolicy.Columns(new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", active.Id, [old, active]), "es");

        Assert.Equal(new[] { block.FieldId, quantity.FieldId }, columns.Select(column => column.FieldId));
        Assert.Equal(new[] { moved.FieldId }, columns[0].Questions!.Select(column => column.FieldId));
        Assert.Equal(2, columns[0].IntroducedInVersion);
    }
    [Fact]
    public void The_block_cell_lists_its_items_with_one_cell_per_question_of_its_column()
    {
        var block = new FormResponseColumn(Guid.NewGuid(), "group", "Artículos", 1, [], Questions: [new(Guid.NewGuid(), "shortText", "Artículo", 1, []),
            new(Guid.NewGuid(), "decimal", "Cantidad", 1, []), new(Guid.NewGuid(), "shortText", "Lote", 2, [])]);
        var (item, quantity) = (block.Questions![0].FieldId, block.Questions[1].FieldId);
        // The version of this response had no «Lote» in its block.
        var rows = new StructuredRecordReadRow[] { new([Value(item, "shortText", "Semilla"), Value(quantity, "decimal", "0")]),
            new([Value(item, "shortText", "Fertilizante"), Value(quantity, "decimal", null)]) };

        var cell = Assert.Single(FormResponsePresentationPolicy.Cells([block], Reply(Value(block.FieldId, "group", rows))));

        Assert.Equal(("group", "Answered"), (cell.Type, cell.State));
        var items = Assert.IsAssignableFrom<IReadOnlyList<FormResponseRowCells>>(cell.Value);
        Assert.Equal(2, items.Count);
        Assert.Equal(new[] { "Answered", "Answered", "NotAsked" }, items[0].Cells.Select(value => value.State));
        Assert.Equal(new[] { "Answered", "Unanswered", "NotAsked" }, items[1].Cells.Select(value => value.State));
        Assert.Equal("0", items[0].Cells[1].Value);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(cell, JsonSerializerOptions.Web));
        Assert.Equal("Fertilizante", json.RootElement.GetProperty("value")[1].GetProperty("cells")[0].GetProperty("value").GetString());
    }
    [Fact]
    public void A_block_without_items_is_unanswered_and_a_version_without_it_was_not_asked()
    {
        var block = new FormResponseColumn(Guid.NewGuid(), "group", "Artículos", 1, [], Questions: [new(Guid.NewGuid(), "shortText", "Artículo", 1, [])]);

        var empty = Assert.Single(FormResponsePresentationPolicy.Cells([block], Reply(Value(block.FieldId, "group", Array.Empty<StructuredRecordReadRow>()))));

        Assert.Equal("Unanswered", empty.State);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<FormResponseRowCells>>(empty.Value));
        Assert.Equal("NotAsked", Assert.Single(FormResponsePresentationPolicy.Cells([block], Reply())).State);
    }
    [Fact]
    public void Missing_question_unanswered_zero_and_false_remain_distinct()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var columns = ids.Select((id, index) => new FormResponseColumn(id, index == 2 ? "decimal" : index == 3 ? "boolean" : "shortText", "Label", 1, [])).ToArray();
        var reply = new StructuredRecordReadReply(Guid.NewGuid(), Guid.NewGuid(), "T-000001", Guid.NewGuid(), Guid.NewGuid(), 1, "Form", [
            new(ids[1], "second", "shortText", "Second", null), new(ids[2], "third", "decimal", "Third", "0"), new(ids[3], "fourth", "boolean", "Fourth", false)]);
        var cells = FormResponsePresentationPolicy.Cells(columns, reply);
        Assert.Equal(new[] { "NotAsked", "Unanswered", "Answered", "Answered" }, cells.Select(cell => cell.State));
        Assert.Equal("0", cells[2].Value);
        Assert.Equal(false, cells[3].Value);
    }
    private static StructuredRecordReadValue Value(Guid fieldId, string type, object? value) => new(fieldId, "key", type, "Label", value);
    private static StructuredRecordReadReply Reply(params StructuredRecordReadValue[] values) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "T-000001", Guid.NewGuid(), Guid.NewGuid(), 1, "Form", values);
    private static Dictionary<string,string> Text(string es,string en) => new() { ["es"]=es,["en"]=en };
    private static CanonicalFormField Field(string type,string es,string en) {var id=Guid.NewGuid();return new(id,"field_"+id.ToString("N"),type,Text(es,en),null,false,type=="shortText"?80:null,null);}
    private static FormResponseVersionRow Version(long number,IReadOnlyList<CanonicalFormField> fields) => new(Guid.NewGuid(),number,JsonSerializer.Serialize(Text("Formulario","Form")),JsonSerializer.Serialize(new FormVersionSchema(1,fields),JsonSerializerOptions.Web), "es", ["es", "en"]);
}
