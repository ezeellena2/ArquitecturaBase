namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;
public sealed record FormExportRequest(ListFormRecordsRequest Filter, IReadOnlyList<Guid>? Columns, IReadOnlyList<FormExportCatalogField?>? CatalogFields = null)
{ public override string ToString() => "FormExportRequest { Content = [REDACTED] }"; }
public sealed record FormExportResponse(byte[] Content, string FileName)
{ public override string ToString() => "FormExportResponse { Content = [REDACTED] }"; }
public sealed record FormWorkbookRequest(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<FormWorkbookCell>> Rows, string SheetName)
{ public override string ToString() => "FormWorkbookRequest { Content = [REDACTED] }"; }
public sealed record FormWorkbookCell(string Kind, object? Value)
{ public override string ToString() => "FormWorkbookCell { Value = [REDACTED] }"; }

public sealed record FormExportCatalogField(Guid QuestionId, Guid FieldId);
