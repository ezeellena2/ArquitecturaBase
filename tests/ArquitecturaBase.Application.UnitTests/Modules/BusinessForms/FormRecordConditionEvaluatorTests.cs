using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormRecordConditionEvaluatorTests
{
    [Fact]
    public void Contains_is_case_insensitive_and_treats_percent_underscore_and_backslash_as_literal_text()
    {
        var field=Field("longText");var snapshot=Snapshot([field],new(){[field.FieldId]="Customer %_\\ detail"});
        Assert.True(FormRecordConditionEvaluator.Matches([new(field.FieldId,"contains",["CUSTOMER %_\\"])],snapshot));
        Assert.False(FormRecordConditionEvaluator.Matches([new(field.FieldId,"contains",["Customer X"])],snapshot));
    }
    [Fact]
    public void Conditions_are_anded_and_preserve_false_zero_and_not_asked()
    {
        var number=Field("decimal");var boolean=Field("boolean");var missing=Guid.NewGuid();
        var snapshot=Snapshot([number,boolean],new(){[number.FieldId]=0m,[boolean.FieldId]=false});
        Assert.True(FormRecordConditionEvaluator.Matches([new(number.FieldId,"equals",["0.00"]),new(boolean.FieldId,"in",["false"])],snapshot));
        Assert.False(FormRecordConditionEvaluator.Matches([new(number.FieldId,"gte",["1"]),new(boolean.FieldId,"in",["false"])],snapshot));
        Assert.True(FormRecordConditionEvaluator.Matches([new(number.FieldId,"answered",null),new(boolean.FieldId,"answered",null)],snapshot));
        Assert.True(FormRecordConditionEvaluator.Matches([new(missing,"notAsked",null)],snapshot));
        Assert.True(FormRecordConditionEvaluator.Matches([new(missing,"unanswered",null)],snapshot));
    }
    [Fact]
    public void Empty_optional_values_and_files_use_the_same_presence_rules_as_the_sheet()
    {
        var text=Field("shortText");var photo=Field("photo");var record=Snapshot([text,photo],new());
        Assert.True(FormRecordConditionEvaluator.Matches([new(text.FieldId,"unanswered",null),new(photo.FieldId,"unanswered",null)],record));
        var file=new FormRecordFileRow(record.Response.Record.RecordId,photo.FieldId,Guid.NewGuid(),0,"photo.png","image/png",10,true);
        record=record with { Response=record.Response with { Record=record.Response.Record with { Files=[file] } } };
        Assert.True(FormRecordConditionEvaluator.Matches([new(photo.FieldId,"answered",null)],record));
        Assert.False(FormRecordConditionEvaluator.Matches([new(photo.FieldId,"unanswered",null)],record));
    }
    [Fact]
    public void Dates_and_choices_compare_the_typed_value_instead_of_a_display_label()
    {
        var date=Field("date");var choice=Field("choice");var option=Guid.NewGuid();
        var snapshot=Snapshot([date,choice],new(){[date.FieldId]="2026-10-16",[choice.FieldId]=option});
        Assert.True(FormRecordConditionEvaluator.Matches([new(date.FieldId,"gte",["2026-10-15"]),new(choice.FieldId,"in",[option.ToString()])],snapshot));
        Assert.False(FormRecordConditionEvaluator.Matches([new(date.FieldId,"lte",["2026-10-15"])],snapshot));
    }
    [Theory]
    [InlineData("shortText", false, false, false, true, true)]
    [InlineData("shortText", true, false, false, true, false)]
    [InlineData("shortText", true, true, true, false, false)]
    [InlineData("photo", false, false, false, true, true)]
    [InlineData("photo", true, false, false, true, false)]
    [InlineData("photo", true, true, true, false, false)]
    public void Presence_has_explicit_expected_results_for_absent_optional_and_answered_questions(
        string type, bool asked, bool hasValue, bool answered, bool unanswered, bool notAsked)
    {
        var field = Field(type);
        var values = type == "shortText" && hasValue ? new Dictionary<Guid, object> { [field.FieldId] = "Recorded" } : [];
        var snapshot = Snapshot(asked ? [field] : [], values);
        if (type == "photo" && hasValue)
        {
            var file = new FormRecordFileRow(snapshot.Response.Record.RecordId, field.FieldId, Guid.NewGuid(), 0, "photo.png", "image/png", 10, true);
            snapshot = snapshot with { Response = snapshot.Response with { Record = snapshot.Response.Record with { Files = [file] } } };
        }
        Assert.Equal(answered, FormRecordConditionEvaluator.Matches([new(field.FieldId, "answered", null)], snapshot));
        Assert.Equal(unanswered, FormRecordConditionEvaluator.Matches([new(field.FieldId, "unanswered", null)], snapshot));
        Assert.Equal(notAsked, FormRecordConditionEvaluator.Matches([new(field.FieldId, "notAsked", null)], snapshot));
    }
    [Fact]
    public void A_condition_on_a_question_of_the_block_is_met_by_any_item()
    {
        var record = Received(Item(SeedId, 200.5m, "Sin daños"), Item(SeedId, 100m), Item(FertilizerId, 7m));
        Assert.True(Matches(record, [new(Quantity.FieldId, "gte", ["150"])]));
        Assert.True(Matches(record, [new(Article.FieldId, "in", [FertilizerId.ToString()])]));
        Assert.True(Matches(record, [new(Note.FieldId, "contains", ["SIN DAÑOS"])]));
        Assert.False(Matches(record, [new(Quantity.FieldId, "gte", ["500"])]));
    }
    [Fact]
    public void Conditions_on_questions_of_the_block_are_met_by_one_same_item()
    {
        var record = Received(Item(SeedId, 200.5m, "Sin daños"), Item(SeedId, 100m), Item(FertilizerId, 7m));
        Assert.True(Matches(record, [new(Article.FieldId, "in", [SeedId.ToString()]), new(Quantity.FieldId, "gte", ["200"])]));
        Assert.False(Matches(record, [new(Article.FieldId, "in", [FertilizerId.ToString()]), new(Quantity.FieldId, "gte", ["200"])]));
    }
    [Fact]
    public void A_condition_outside_the_block_is_met_apart_from_the_items()
    {
        var record = Received(Item(SeedId, 200.5m, "Sin daños"), Item(SeedId, 100m), Item(FertilizerId, 7m));
        Assert.True(Matches(record, [new(Remito.FieldId, "equals", ["0003-00004521"]), new(Quantity.FieldId, "gte", ["200"])]));
        Assert.False(Matches(record, [new(Remito.FieldId, "equals", ["0003-00000001"]), new(Quantity.FieldId, "gte", ["200"])]));
    }
    [Fact]
    public void Presence_on_a_question_of_the_block_looks_at_the_whole_response()
    {
        Assert.Equal((true, false, false), Presence(Received(Item(SeedId, 200.5m, "Sin daños"), Item(FertilizerId, 7m)), Note));
        Assert.Equal((false, true, false), Presence(Received(Item(SeedId, 100m), Item(FertilizerId, 7m)), Note));
        Assert.Equal((false, true, true), Presence(BeforeTheBlock(), Note));
    }
    [Fact]
    public void A_block_without_items_and_a_response_from_before_the_block_meet_no_condition_with_a_value()
    {
        var withoutEntry = Snapshot(BlockFields, new() { [Remito.FieldId] = "0003-00004521" });
        foreach (var record in new[] { Received(), withoutEntry, BeforeTheBlock() })
        {
            Assert.False(Matches(record, [new(Quantity.FieldId, "gte", ["0"])]));
            Assert.False(Matches(record, [new(Article.FieldId, "in", [SeedId.ToString()])]));
            Assert.True(Matches(record, [new(Note.FieldId, "unanswered", null)]));
        }
    }
    // A block of rows (RG5-A): its questions are repeated, and each item holds their answers.
    private static readonly Guid SeedId = Guid.NewGuid();
    private static readonly Guid FertilizerId = Guid.NewGuid();
    private static readonly CanonicalFormField Remito = Field("shortText");
    private static readonly CanonicalFormField Block = Field("group") with { Rows = new(0, 10) };
    private static readonly CanonicalFormField Article = Field("choice") with { Repeated = true };
    private static readonly CanonicalFormField Quantity = Field("decimal") with { Repeated = true };
    private static readonly CanonicalFormField Note = Field("longText") with { Repeated = true };
    private static readonly CanonicalFormField[] BlockFields = [Remito, Block, Article, Quantity, Note];
    private static FormActionRecordRow Received(params object[][] items) =>
        Snapshot(BlockFields, new() { [Remito.FieldId] = "0003-00004521", [Block.FieldId] = items });
    private static FormActionRecordRow BeforeTheBlock() => Snapshot([Remito], new() { [Remito.FieldId] = "0003-00004521" });
    private static object[] Item(Guid article, decimal quantity, string? note = null) => note is null
        ? new object[] { new { fieldId = Article.FieldId, value = article }, new { fieldId = Quantity.FieldId, value = quantity } }
        : new object[] { new { fieldId = Article.FieldId, value = article }, new { fieldId = Quantity.FieldId, value = quantity }, new { fieldId = Note.FieldId, value = note } };
    private static bool Matches(FormActionRecordRow record, IReadOnlyList<FormRecordCondition> conditions) => FormRecordConditionEvaluator.Matches(conditions, record);
    private static (bool Answered, bool Unanswered, bool NotAsked) Presence(FormActionRecordRow record, CanonicalFormField field) =>
        (Matches(record, [new(field.FieldId, "answered", null)]), Matches(record, [new(field.FieldId, "unanswered", null)]),
            Matches(record, [new(field.FieldId, "notAsked", null)]));
    private static CanonicalFormField Field(string type)=>new(Guid.NewGuid(),type,type,new Dictionary<string,string>{{"es",type},{"en",type}},null,false,null,null);
    private static FormActionRecordRow Snapshot(IReadOnlyList<CanonicalFormField> fields,Dictionary<Guid,object> values)=>new(
        new(new(Guid.NewGuid(),Guid.NewGuid(),1,Guid.NewGuid(),"T",Guid.NewGuid(),1,"{\"es\":\"Form\",\"en\":\"Form\"}",
            JsonSerializer.Serialize(new FormVersionSchema(1,fields),JsonSerializerOptions.Web),JsonSerializer.Serialize(values.Select(pair=>new{fieldId=pair.Key,value=pair.Value}))),
            new DateTime(2026,10,16,12,0,0,DateTimeKind.Utc),new(Guid.NewGuid(),"Author")),"es");
}
