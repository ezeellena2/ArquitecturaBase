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
        Assert.True(FormRecordConditionEvaluator.Matches([new(missing,"notAsked",null)],snapshot));
        Assert.False(FormRecordConditionEvaluator.Matches([new(missing,"unanswered",null)],snapshot));
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
    private static CanonicalFormField Field(string type)=>new(Guid.NewGuid(),type,type,new Dictionary<string,string>{{"es",type},{"en",type}},null,false,null,null);
    private static FormActionRecordSnapshot Snapshot(IReadOnlyList<CanonicalFormField> fields,Dictionary<Guid,object> values)=>new(
        new(new(Guid.NewGuid(),Guid.NewGuid(),1,Guid.NewGuid(),"T",Guid.NewGuid(),1,"{\"es\":\"Form\",\"en\":\"Form\"}",
            JsonSerializer.Serialize(new FormVersionSchema(1,fields),JsonSerializerOptions.Web),JsonSerializer.Serialize(values.Select(pair=>new{fieldId=pair.Key,value=pair.Value}))),
            new DateTime(2026,10,16,12,0,0,DateTimeKind.Utc),new(Guid.NewGuid(),"Author")),"es");
}
