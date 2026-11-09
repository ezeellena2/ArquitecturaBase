using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>A stored result (RE1-A) in a language: the names it was sealed with, the other language when one is missing.</summary>
internal static class FormRecordResultPresentationPolicy
{
    public static FormResponseResultResponse Project(FormRecordResultRow row, string culture)
    {
        var result = FormRecordResultRecorder.Read(row.Source, row.SourceNameJson, row.ValuesJson);
        return new(result.Source, FormRecordPresentationPolicy.Text(result.SourceName, culture), row.RecordedAtUtc,
            result.Values.Select(value => new FormResponseResultValueResponse(value.Key, FormRecordPresentationPolicy.Text(value.Label, culture),
                value.Kind, value.Value)).ToArray());
    }
}
