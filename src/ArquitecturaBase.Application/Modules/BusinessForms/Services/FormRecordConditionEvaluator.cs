using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormRecordConditionEvaluator
{
    public static bool Matches(IReadOnlyList<FormRecordCondition> conditions,FormActionRecordSnapshot snapshot)
    {
        var row=snapshot.Response.Record;var schema=FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
        using var values=JsonDocument.Parse(row.ValuesJson);
        var answers=values.RootElement.EnumerateArray().ToDictionary(value=>value.GetProperty("fieldId").GetGuid(),value=>value.GetProperty("value"));
        return conditions.All(condition=>Matches(condition,schema,answers,row.Files??[]));
    }
    private static bool Matches(FormRecordCondition condition,FormVersionSchema schema,Dictionary<Guid,JsonElement> answers,IReadOnlyList<FormRecordFileRow> files)
    {
        var field=schema.Questions.SingleOrDefault(field=>field.FieldId==condition.FieldId);
        var present=answers.TryGetValue(condition.FieldId,out var answer)||files.Any(file=>file.FieldId==condition.FieldId);
        if(condition.Operator=="notAsked")return field is null;
        if(condition.Operator=="answered")return present;
        if(condition.Operator=="unanswered")return field is not null&&!present;
        if(field is null||!answers.ContainsKey(condition.FieldId))return false;
        var expected=condition.Values![0];
        return field.Type switch
        {
            "shortText" or "longText"=>condition.Operator=="contains"?answer.GetString()!.Contains(expected,StringComparison.OrdinalIgnoreCase):answer.GetString()==expected,
            "decimal"=>Compare(answer.GetDecimal().CompareTo(decimal.Parse(expected,CultureInfo.InvariantCulture)),condition.Operator),
            "date"=>Compare(DateOnly.ParseExact(answer.GetString()!,"yyyy-MM-dd",CultureInfo.InvariantCulture).CompareTo(DateOnly.ParseExact(expected,"yyyy-MM-dd",CultureInfo.InvariantCulture)),condition.Operator),
            "boolean"=>condition.Values.Contains(answer.GetBoolean()?"true":"false"),
            "choice"=>condition.Values.Any(value=>Guid.Parse(value)==answer.GetGuid()),
            _=>false,
        };
    }
    private static bool Compare(int compared,string operation)=>operation switch {"gte"=>compared>=0,"lte"=>compared<=0,_=>compared==0};
}
