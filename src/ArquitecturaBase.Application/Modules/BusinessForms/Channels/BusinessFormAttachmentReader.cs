using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

// Stage 2 of the assistant authoring: Home and a channel read a spreadsheet through the neutral port. Who manages catalogs
// attaches one; the import reader says what its bytes are and reads them as that, with its limits, and nothing is stored.
internal sealed class BusinessFormAttachmentReader(FormCatalogAccessGuard access, IFormSpreadsheetReader spreadsheets) : IStructuredAttachmentReader
{
    private static readonly string[] Limits = [FormCatalogImportErrors.FileTooLarge.Code, FormCatalogImportErrors.TooManyRows.Code,
        FormCatalogImportErrors.TooManyColumns.Code, FormCatalogImportErrors.TooManyCells.Code];

    public async Task<bool> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        (await access.CheckActorAsync(actorUserId, writing: true, cancellationToken)).IsSuccess;

    public Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Prepare(content, FormAttachmentPreparationPolicy.FileName(fileName), cancellationToken));
    }

    private StructuredAttachmentPreparation Prepare(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        if (content.Length > FormCatalogImportLimits.MaximumFileBytes) return Rejected(StructuredAttachmentRejection.TooLarge);
        var format = spreadsheets.DetectFormat(content);
        StructuredSpreadsheetKind kind;
        switch (format)
        {
            case FormSpreadsheetFormat.Xlsx: kind = StructuredSpreadsheetKind.Xlsx; break;
            case FormSpreadsheetFormat.Csv: kind = StructuredSpreadsheetKind.Csv; break;
            case FormSpreadsheetFormat.MacroEnabled: return Rejected(StructuredAttachmentRejection.MacroEnabled);
            case FormSpreadsheetFormat.Legacy: return Rejected(StructuredAttachmentRejection.Legacy);
            case FormSpreadsheetFormat.Protected: return Rejected(StructuredAttachmentRejection.Protected);
            default: return Rejected(StructuredAttachmentRejection.Unreadable);
        }
        var sheet = spreadsheets.Read(content, format, cancellationToken);
        if (sheet.IsFailure)
            return Rejected(Limits.Contains(sheet.Error.Code) ? StructuredAttachmentRejection.TooLarge : StructuredAttachmentRejection.Unreadable);
        return FormAttachmentPreparationPolicy.Prepare(fileName, kind, content.LongLength, sheet.Value);
    }

    private static StructuredAttachmentPreparation Rejected(StructuredAttachmentRejection rejection) =>
        StructuredAttachmentPreparation.Rejected(rejection);
}
