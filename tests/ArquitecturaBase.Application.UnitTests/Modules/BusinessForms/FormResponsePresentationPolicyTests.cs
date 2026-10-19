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
    private static Dictionary<string,string> Text(string es,string en) => new() { ["es"]=es,["en"]=en };
    private static CanonicalFormField Field(string type,string es,string en) {var id=Guid.NewGuid();return new(id,"field_"+id.ToString("N"),type,Text(es,en),null,false,type=="shortText"?80:null,null);}
    private static FormResponseVersionRow Version(long number,IReadOnlyList<CanonicalFormField> fields) => new(Guid.NewGuid(),number,JsonSerializer.Serialize(Text("Formulario","Form")),JsonSerializer.Serialize(new FormVersionSchema(1,fields),JsonSerializerOptions.Web), "es", ["es", "en"]);
}
