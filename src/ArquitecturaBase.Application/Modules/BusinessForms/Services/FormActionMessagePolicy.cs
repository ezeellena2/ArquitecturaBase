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
    /// <summary>
    /// A question token holds its FieldId (MD1-A, 2026-11-02), so it survives a renamed label and serves both languages.
    /// Earlier revisions hold the label in the text's culture, resolved with the questions fixed by the revision.
    /// </summary>
    public static Guid? Reference(string token) => Guid.TryParseExact(token, "D", out var fieldId) ? fieldId : null;
    /// <summary>The rules with every question token as its FieldId, a label resolved with the questions the revision fixed.</summary>
    public static IReadOnlyList<FormRule> WithReferences(IReadOnlyList<FormRule> rules, FormVersionSchema questions) =>
        rules.Select(rule => rule with { Actions = rule.Actions.Select(action => action.Text is null ? action : action with
        {
            Text = action.Text.ToDictionary(text => text.Key, text => WithReferences(text.Value, text.Key, questions))
        }).ToArray() }).ToArray();
    private static string WithReferences(string template, string culture, FormVersionSchema questions)
    {
        var result = new StringBuilder();
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] != '{') { result.Append(template[index]); continue; }
            var end = template.IndexOf('}', index + 1);
            var token = template[(index + 1)..end];
            result.Append('{').Append(token is "número" or "number" or "motivo" or "reason" || Reference(token) is not null
                || FormActionResultTokenPolicy.IsResult(token) ? token
                : questions.Questions.Single(question => question.Label.GetValueOrDefault(culture) == token).FieldId.ToString("D")).Append('}');
            index = end;
        }
        return result.ToString();
    }
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
                if (token is not ("número" or "number" or "motivo" or "reason") && !FormActionResultTokenPolicy.IsResult(token))
                    used.Add(Reference(token) ?? questions.Questions.Single(question => question.Label.GetValueOrDefault(text.Key) == token).FieldId);
                index = end;
            }
        }
        return used;
    }

    public static string Render(FormRuleAction action,FormVersionSchema ruleQuestions,FormActionRecordRow snapshot,string culture)
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
            // A datum of a result (RE1-A) does not exist yet: it stays marked and is filled in when the message is sent.
            else if(FormActionResultTokenPolicy.IsResult(token))result.Append(FormActionResultTokenPolicy.Mark(token));
            else
            {
                var fieldId=Reference(token)??ruleQuestions.Questions.Single(question=>question.Label.GetValueOrDefault(culture)==token).FieldId;
                if(answers.TryGetValue(fieldId,out var answer))Append(result,answer,culture);
            }
            index=end;
        }
        return result.ToString();
    }
    /// <summary>
    /// An answer of the response. The block of rows writes one item per line (RG5-A), and its first item starts on a new line
    /// unless the message starts there or a line break comes before it.
    /// </summary>
    private static void Append(StringBuilder result,StructuredRecordReadValue answer,string culture)
    {
        if(answer.Value is not IReadOnlyList<StructuredRecordReadRow> items){result.Append(Value(answer,culture));return;}
        if(items.Count>0&&result.Length>0&&result[^1]!='\n')result.Append('\n');
        result.AppendJoin('\n',items.Select((item,index)=>Item(item,index,culture)));
    }
    /// <summary>An item, numbered from 1, with its answers in the order of its block joined by « · » and without the empty ones.</summary>
    private static string Item(StructuredRecordReadRow item,int index,string culture)
    {
        var number=(index+1).ToString(CultureInfo.InvariantCulture)+".";
        var answers=string.Join(" · ",item.Values.Select(value=>Value(value,culture)).Where(text=>text.Length>0));
        return answers.Length==0?number:number+" "+answers;
    }
    // An answer never carries the mark of a datum of a result (RE1-A): only the template places one.
    private static string Value(StructuredRecordReadValue answer,string culture)=>FormActionResultTokenPolicy.Unmarked(RawValue(answer,culture));
    private static string RawValue(StructuredRecordReadValue answer,string culture)=>answer.Value switch
    {
        null=>"",bool value=>FormResponseTexts.Get(value?"true":"false",culture),StructuredRecordChoiceValue value=>value.Label,
        IReadOnlyList<StructuredRecordChoiceValue> choices=>string.Join(", ",choices.Select(choice=>choice.Label)),
        IReadOnlyList<StructuredRecordFileValue> files=>string.Join(", ",files.Select(file=>file.Name)),
        // UB4-A: WhatsApp has no text with a link, so the point goes with the link to the map on the line below.
        StructuredRecordLocationValue location=>location.Coordinates()+"\n"+location.MapUrl(),
        string value when answer.Type=="decimal"=>decimal.Parse(value,CultureInfo.InvariantCulture).ToString("0.############################",CultureInfo.GetCultureInfo(culture)),
        // A date is written with numbers only, two digits for the day and the month, in the order of the language.
        string value when answer.Type=="date"=>DateOnly.ParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture)
            .ToString(culture=="en"?"MM/dd/yyyy":"dd/MM/yyyy",CultureInfo.InvariantCulture),
        string value=>value,_=>throw new InvalidOperationException("The action value type is unsupported."),
    };
}
