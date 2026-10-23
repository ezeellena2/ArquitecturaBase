using System.Text.Json.Serialization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public static class FormCatalogImportLimits
{
    public const int MaximumFileBytes = 10 * 1024 * 1024;
    public const int MaximumArchiveBytes = 100 * 1024 * 1024;
    public const int MaximumColumns = 64;
    public const int MaximumPhysicalRows = BusinessFormCatalog.MaxItems + 2;
    public const int MaximumPhysicalCells = MaximumPhysicalRows * MaximumColumns;
    public const int MaximumPreviewRows = 100;
}

public enum FormCatalogImportMode { Replace, Append }

public sealed record FormSpreadsheetCell(string? Text, Error? Error = null, bool IsCanonicalValue = false)
{ public override string ToString() => "FormSpreadsheetCell { Content = [REDACTED] }"; }
public sealed record FormSpreadsheetRow(int Number, IReadOnlyList<FormSpreadsheetCell> Cells)
{ public override string ToString() => "FormSpreadsheetRow { Content = [REDACTED] }"; }
public sealed record FormSpreadsheet(string Name, string Sha256, IReadOnlyList<FormSpreadsheetRow> Rows)
{ public override string ToString() => "FormSpreadsheet { Content = [REDACTED] }"; }

public sealed record FormCatalogImportRequest(long ExpectedRevision, string Mode, Stream Content, string FileName,
    string? SheetSha256 = null, int? ConfirmDeactivations = null, bool ConfirmEmpty = false, bool ConfirmErrors = false)
{ public override string ToString() => "FormCatalogImportRequest { Content = [REDACTED] }"; }

public sealed record FormCatalogImportCounts(int Added, int Changed, int Deactivated, int Reactivated,
    int Unchanged, int ErrorRows, int Warnings);
public sealed record FormCatalogImportIssue(int Row, string Column, string Severity, string Code,
    string Message, int? RelatedRow = null, int? Length = null)
{ public override string ToString() => "FormCatalogImportIssue { Content = [REDACTED] }"; }
public sealed record FormCatalogImportAddedRow(int Row, string Code, string Text, string? Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string?>? Fields = null)
{ public override string ToString() => "FormCatalogImportAddedRow { Content = [REDACTED] }"; }
public sealed record FormCatalogImportChange(string Column, string? Before, string? After)
{ public override string ToString() => "FormCatalogImportChange { Content = [REDACTED] }"; }
public sealed record FormCatalogImportChangedRow(int Row, Guid ItemId, string Code, IReadOnlyList<FormCatalogImportChange> Changes)
{ public override string ToString() => "FormCatalogImportChangedRow { Content = [REDACTED] }"; }
public sealed record FormCatalogImportDeactivatedRow(Guid ItemId, string Code, string Text)
{ public override string ToString() => "FormCatalogImportDeactivatedRow { Content = [REDACTED] }"; }
public sealed record FormCatalogImportConfirmations(int Deactivations, bool EmptySheet, bool ManyErrors);
public sealed record FormCatalogImportPreviewResponse(Guid CatalogId, long Revision, string Mode, string SheetSha256,
    string SheetName, int RowsRead, FormCatalogImportCounts Counts, IReadOnlyList<FormCatalogImportIssue> Issues,
    IReadOnlyList<FormCatalogImportAddedRow> Added, IReadOnlyList<FormCatalogImportChangedRow> Changed,
    IReadOnlyList<FormCatalogImportDeactivatedRow> Deactivated, bool ListsTruncated,
    FormCatalogImportConfirmations Confirmations, DateTime? AlreadyAppliedAtUtc)
{ public override string ToString() => "FormCatalogImportPreviewResponse { Content = [REDACTED] }"; }

internal sealed record FormCatalogImportSource(Guid Id, long Revision, BusinessFormCatalogValueType ValueType,
    IReadOnlyList<FormCatalogStoredItemRow> Items, string? LastImportSha256 = null, DateTime? LastImportAtUtc = null,
    IReadOnlyList<BusinessFormCatalogField>? Fields = null, IReadOnlyDictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>? Sources = null)
{ public override string ToString() => "FormCatalogImportSource { Content = [REDACTED] }"; }
internal sealed record FormCatalogImportPlan(FormCatalogImportPreviewResponse Preview,
    IReadOnlyList<FormCatalogManualItemSaveRequest> Items)
{ public override string ToString() => "FormCatalogImportPlan { Content = [REDACTED] }"; }
