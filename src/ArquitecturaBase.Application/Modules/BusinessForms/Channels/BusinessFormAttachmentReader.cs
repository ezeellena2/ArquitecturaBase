using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

// Stage 2 of the assistant authoring: Home and a channel read a spreadsheet through the neutral port. Who manages catalogs
// attaches one; the import reader reads it by what its bytes are, with its limits, and nothing is stored.
internal sealed class BusinessFormAttachmentReader(FormCatalogAccessGuard access, IFormSpreadsheetReader spreadsheets) : IStructuredAttachmentReader
{
    private static readonly string[] Limits = [FormCatalogImportErrors.FileTooLarge.Code, FormCatalogImportErrors.TooManyRows.Code,
        FormCatalogImportErrors.TooManyColumns.Code, FormCatalogImportErrors.TooManyCells.Code];

    public async Task<bool> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        (await access.CheckActorAsync(actorUserId, writing: true, cancellationToken)).IsSuccess;

    public async Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);
        cancellationToken.ThrowIfCancellationRequested();
        if (content.Length > FormCatalogImportLimits.MaximumFileBytes) return Rejected(StructuredAttachmentRejection.TooLarge);
        StructuredSpreadsheetKind kind;
        switch (spreadsheets.DetectFormat(content))
        {
            case FormSpreadsheetFormat.Xlsx: kind = StructuredSpreadsheetKind.Xlsx; break;
            case FormSpreadsheetFormat.Csv: kind = StructuredSpreadsheetKind.Csv; break;
            case FormSpreadsheetFormat.MacroEnabled: return Rejected(StructuredAttachmentRejection.MacroEnabled);
            case FormSpreadsheetFormat.LegacyOrProtected: return Rejected(StructuredAttachmentRejection.LegacyOrProtected);
            default: return Rejected(StructuredAttachmentRejection.Unreadable);
        }
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        using var stream = new MemoryStream(content, writable: false);
        // The import reader decides by the extension: it gets the one of what the bytes are, with the person's file name.
        var sheet = await spreadsheets.ReadAsync(stream,
            Path.GetFileNameWithoutExtension(name) + (kind == StructuredSpreadsheetKind.Xlsx ? ".xlsx" : ".csv"), cancellationToken);
        if (sheet.IsFailure)
            return Rejected(Limits.Contains(sheet.Error.Code) ? StructuredAttachmentRejection.TooLarge : StructuredAttachmentRejection.Unreadable);
        return FormAttachmentPreparationPolicy.Prepare(name, kind, content.LongLength, sheet.Value);
    }

    private static StructuredAttachmentPreparation Rejected(StructuredAttachmentRejection rejection) =>
        StructuredAttachmentPreparation.Rejected(rejection);
}
