using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>A stored result (RE1-A) in a language: the names it was sealed with, the other language when one is missing.</summary>
internal static class FormRecordResultPresentationPolicy
{
    /// <summary>The sheet's columns for the results, by source and in the order of its latest result's values.</summary>
    public static IReadOnlyList<FormResponseResultColumn> Columns(IReadOnlyList<FormRecordResultRow> latest, string culture) =>
        latest.OrderBy(row => row.Source, StringComparer.Ordinal).SelectMany(row =>
        {
            var result = FormRecordResultRecorder.Read(row.Source, row.SourceNameJson, row.ValuesJson);
            var name = FormRecordPresentationPolicy.Text(result.SourceName, culture);
            return result.Values.Select(value => new FormResponseResultColumn(ColumnId(result.Source, value.Key), result.Source, name, value.Key,
                FormRecordPresentationPolicy.Text(value.Label, culture), value.Kind));
        }).ToArray();

    /// <summary>The values of each response by column id; a later result of the same source wins.</summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string?>> Values(IReadOnlyList<FormRecordResultRow> rows) =>
        rows.GroupBy(row => row.RecordId).ToDictionary(group => group.Key, group => (IReadOnlyDictionary<string, string?>)group
            .OrderBy(row => row.RecordedAtUtc).SelectMany(row => FormRecordResultRecorder.Read(row.Source, row.SourceNameJson, row.ValuesJson) is var result
                ? result.Values.Select(value => (Id: ColumnId(result.Source, value.Key), value.Value)) : [])
            .GroupBy(pair => pair.Id).ToDictionary(pair => pair.Key, pair => pair.Last().Value));

    public static string ColumnId(string source, string key) => source + "." + key;

    /// <summary>The results for the PDF copy, in a language; the renderer writes each value like an answer of its kind.</summary>
    public static IReadOnlyList<FormPdfResult> Pdf(IReadOnlyList<FormRecordResultRow> rows, string culture) =>
        rows.OrderBy(row => row.RecordedAtUtc).Select(row => FormRecordResultRecorder.Read(row.Source, row.SourceNameJson, row.ValuesJson)).Select(result =>
            new FormPdfResult(FormRecordPresentationPolicy.Text(result.SourceName, culture), result.Values.Select(value =>
                new FormPdfResultValue(FormRecordPresentationPolicy.Text(value.Label, culture), value.Kind, value.Value)).ToArray())).ToArray();

    public static FormResponseResultResponse Project(FormRecordResultRow row, string culture)
    {
        var result = FormRecordResultRecorder.Read(row.Source, row.SourceNameJson, row.ValuesJson);
        return new(result.Source, FormRecordPresentationPolicy.Text(result.SourceName, culture), row.RecordedAtUtc,
            result.Values.Select(value => new FormResponseResultValueResponse(value.Key, FormRecordPresentationPolicy.Text(value.Label, culture),
                value.Kind, value.Value)).ToArray());
    }
}
