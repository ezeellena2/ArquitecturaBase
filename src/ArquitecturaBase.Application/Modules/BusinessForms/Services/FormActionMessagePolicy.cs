using System.Globalization;
using System.Text;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormActionMessagePolicy
{
    public static IReadOnlyList<string> CompleteCultures(IReadOnlyList<FormRule> rules, string original) =>
        new[] { original, original == "es" ? "en" : "es" }
            .Where(culture => rules.SelectMany(rule => rule.Actions)
                .Where(action => action.Kind is "NotifyAuthor" or "NotifyRole")
                .All(action => !string.IsNullOrWhiteSpace(action.Text!.GetValueOrDefault(culture))))
            .ToArray();

    public static string Culture(
        string requested, string original, IReadOnlyList<string> completeCultures, IReadOnlyList<string>? pdfCultures) =>
        completeCultures.Contains(requested) && (pdfCultures is null || pdfCultures.Contains(requested)) ? requested : original;
    public static HashSet<Guid> UsedFields(IReadOnlyList<FormRule> rules, FormVersionSchema questions)
    {
        var used = new HashSet<Guid>();
        foreach (var text in rules.SelectMany(rule => rule.Actions).Where(action => action.Text is not null)
            .SelectMany(action => action.Text!))
        {
            var template = text.Value;
            for (var index = 0; index < template.Length; index++)
            {
                if (template[index] != '{') continue;
                var end = template.IndexOf('}', index + 1);
                var token = template[(index + 1)..end];
                if (token is not ("número" or "number" or "motivo" or "reason"))
                    used.Add(questions.Questions.Single(question => question.Label.GetValueOrDefault(text.Key) == token).FieldId);
                index = end;
            }
        }
        return used;
    }

    public static string Render(FormRuleAction action,FormVersionSchema ruleQuestions,FormActionRecordSnapshot snapshot,string culture)
    {
        var row=snapshot.Response.Record;var schema=FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
        var reply=FormRecordPresentationPolicy.Project(row,culture,schema);var answers=reply.Values.ToDictionary(value=>value.FieldId);
        var template=action.Text![culture];var result=new StringBuilder();
        for(var index=0;index<template.Length;index++)
        {
            if(template[index]!='{'){result.Append(template[index]);continue;}
            var end=template.IndexOf('}',index+1);var token=template[(index+1)..end];
            if(token is "número" or "number")result.Append(reply.Number);
            else if(token is "motivo" or "reason")result.Append(snapshot.Decision?.Reason??"");
            else
            {
                var question=ruleQuestions.Questions.Single(question=>question.Label.GetValueOrDefault(culture)==token);
                if(answers.TryGetValue(question.FieldId,out var answer))result.Append(Value(answer,culture));
            }
            index=end;
        }
        return result.ToString();
    }
    private static string Value(StructuredRecordReadValue answer,string culture)=>answer.Value switch
    {
        null=>"",bool value=>FormResponseTexts.Get(value?"true":"false",culture),StructuredRecordChoiceValue value=>value.Label,
        IReadOnlyList<StructuredRecordFileValue> files=>string.Join(", ",files.Select(file=>file.Name)),
        string value when answer.Type=="decimal"=>decimal.Parse(value,CultureInfo.InvariantCulture).ToString("0.############################",CultureInfo.GetCultureInfo(culture)),
        string value when answer.Type=="date"=>DateOnly.ParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture).ToString("d",CultureInfo.GetCultureInfo(culture)),
        string value=>value,_=>throw new InvalidOperationException("The action value type is unsupported."),
    };
}
