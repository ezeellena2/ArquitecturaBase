using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

// Home and a channel read an attachment through the neutral port. A spreadsheet is attached by whoever manages catalogs (Stage 2 of
// the assistant authoring) and a document (photo, PDF or Word) by whoever designs forms, the only one whose capabilities use it. The
// bytes say what the file is: the document reader goes first (its signatures and manifest types never match a spreadsheet) and the
// import reader reads whatever is not a document, as before, with its limits. Nothing is stored.
internal sealed class BusinessFormAttachmentReader(FormCatalogAccessGuard catalogs, FormDraftAccessGuard forms, IFormSpreadsheetReader spreadsheets,
    IFormDocumentReader documents) : IStructuredAttachmentReader
{
    private static readonly string[] Limits = [FormCatalogImportErrors.FileTooLarge.Code, FormCatalogImportErrors.TooManyRows.Code,
        FormCatalogImportErrors.TooManyColumns.Code, FormCatalogImportErrors.TooManyCells.Code];

    public async Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken) => new(
        (await catalogs.CheckActorAsync(actorUserId, writing: true, cancellationToken)).IsSuccess,
        (await forms.CheckActorAsync(actorUserId, writing: true, cancellationToken)).IsSuccess);

    // Over the limit nothing is opened, so nothing says what the file was.
    public async Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);
        cancellationToken.ThrowIfCancellationRequested();
        if (content.Length > StructuredAttachmentLimits.MaximumBytes) return Rejected(StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Unknown);
        var name = FormAttachmentPreparationPolicy.FileName(fileName);
        var document = documents.DetectFormat(content);
        if (document == FormDocumentFormat.Unknown) return PrepareSpreadsheet(content, fileName, name, cancellationToken);
        return FormDocumentPreparationPolicy.Reject(document)
            ?? FormDocumentPreparationPolicy.Prepare(name, content, document, await documents.ReadAsync(content, document, cancellationToken));
    }

    private StructuredAttachmentPreparation PrepareSpreadsheet(byte[] content, string fileName, string name, CancellationToken cancellationToken)
    {
        var format = spreadsheets.DetectFormat(content);
        StructuredSpreadsheetKind kind;
        switch (format)
        {
            case FormSpreadsheetFormat.Xlsx: kind = StructuredSpreadsheetKind.Xlsx; break;
            case FormSpreadsheetFormat.Csv: kind = StructuredSpreadsheetKind.Csv; break;
            case FormSpreadsheetFormat.MacroEnabled: return Rejected(StructuredAttachmentRejection.MacroEnabled, StructuredAttachmentFileKind.Spreadsheet);
            case FormSpreadsheetFormat.Legacy: return Rejected(StructuredAttachmentRejection.Legacy, StructuredAttachmentFileKind.Spreadsheet);
            case FormSpreadsheetFormat.Protected: return Rejected(StructuredAttachmentRejection.Protected, FormAttachmentPreparationPolicy.ProtectedKind(fileName));
            default: return Rejected(StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Unknown);
        }
        var sheet = spreadsheets.Read(content, format, cancellationToken);
        if (sheet.IsFailure)
            return Rejected(Limits.Contains(sheet.Error.Code) ? StructuredAttachmentRejection.TooLarge : StructuredAttachmentRejection.Unreadable,
                StructuredAttachmentFileKind.Spreadsheet);
        return FormAttachmentPreparationPolicy.Prepare(name, kind, content.LongLength, sheet.Value);
    }

    private static StructuredAttachmentPreparation Rejected(StructuredAttachmentRejection rejection, StructuredAttachmentFileKind kind) =>
        StructuredAttachmentPreparation.Rejected(rejection, kind);
}
