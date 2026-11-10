namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;
public sealed record FormExportRequest(ListFormRecordsRequest Filter, IReadOnlyList<Guid>? Columns, IReadOnlyList<FormExportCatalogField?>? CatalogFields = null)
{ public override string ToString() => "FormExportRequest { Content = [REDACTED] }"; }
public sealed record FormExportResponse(byte[] Content, string FileName)
{ public override string ToString() => "FormExportResponse { Content = [REDACTED] }"; }
public sealed record FormWorkbookRequest(IReadOnlyList<FormWorkbookSheet> Sheets)
{ public override string ToString() => "FormWorkbookRequest { Content = [REDACTED] }"; }
/// <summary>
/// A sheet of the workbook, in its order: headers, rows and a name Excel accepts that no other sheet has.
/// <paramref name="GroupStarts"/> are the rows where a group starts (a response with a row per item, XL1-A); the date
/// formats are the ones of a date and of a moment without a format of their own.
/// </summary>
public sealed record FormWorkbookSheet(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<FormWorkbookCell>> Rows, string Name,
    IReadOnlyList<int>? GroupStarts = null, string? DateFormat = null, string? DateTimeFormat = null)
{ public override string ToString() => "FormWorkbookSheet { Content = [REDACTED] }"; }
// Format is the cell's Excel number format; without it, the writer uses the one of its kind.
public sealed record FormWorkbookCell(string Kind, object? Value, string? Format = null)
{ public override string ToString() => "FormWorkbookCell { Value = [REDACTED] }"; }

public sealed record FormExportCatalogField(Guid QuestionId, Guid FieldId);
