using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// Stage 2 of the assistant authoring: the spreadsheet a proposal mapped, read from the private copy of its turn as the import reads
// an upload. The proposal is read again on the server's side: a propose-catalog@2 with a mapping counts only for the catalog
// created from it, and a propose-import@1 for the catalog it names while no later proposal replaced it; the lookup finds only the
// person's own turns. The copy has to be the same file (its size and its SHA-256) or the person uploads it. Each mapped column
// becomes the import's column with its header, so the review, the confirmations and the marker are an upload's.
internal sealed class FormCatalogAssistantImportPolicy(IAssistantProposalLookup lookup, IPrivateFileStore files, IFormSpreadsheetReader spreadsheets)
{
    // A mapped column of the spreadsheet: its letter and the import's header for what it fills.
    public sealed record Column(string Letter, string Header);

    // What a proposal lets the person import: what it maps, the copy of its turn and the key of the field a column fills.
    private sealed record Source(FormAuthoringSpreadsheetSummary Summary, AssistantAttachmentDescriptor Copy, Func<FormAuthoringMappedColumn, string?> FieldKey);

    // The mapped sheet of a proposal the person can import into this catalog.
    public async Task<Result<FormSpreadsheet>> ReadAsync(Guid actor, FormCatalogStoredRow catalog, Guid requestId, Guid proposalId,
        CancellationToken cancellationToken)
    {
        var source = await FindAsync(actor, catalog, requestId, proposalId, cancellationToken);
        if (source.IsFailure) return source.Error;
        var content = await OpenAsync(source.Value.Copy, cancellationToken);
        if (content.IsFailure) return content.Error;
        var format = spreadsheets.DetectFormat(content.Value);
        if (format is not (FormSpreadsheetFormat.Xlsx or FormSpreadsheetFormat.Csv)) return FormCatalogImportErrors.FileUnreadable;
        var sheet = spreadsheets.Read(content.Value, format, cancellationToken);
        if (sheet.IsFailure) return sheet.Error;
        // The headers' language does not matter: the import reads es and en.
        var columns = Columns(source.Value.Summary, Enum.Parse<BusinessFormCatalogValueType>(catalog.ValueType), catalog.OriginalCulture,
            FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson), source.Value.FieldKey);
        // A CSV has no sheet: the review names it after its file, as an upload's.
        var read = sheet.Value.Name.Length > 0 ? sheet.Value
            : sheet.Value with { Name = Path.GetFileNameWithoutExtension(source.Value.Summary.FileName) is { Length: > 0 } name ? name : "CSV" };
        return Sheet(read, source.Value.Summary.HasHeaders, columns);
    }

    // What a catalog created from a proposal with a spreadsheet still has to import, for the person who asked for it, while it is
    // pending and without options and its proposal is there; null otherwise. The copy is not opened: the review checks it and says
    // when it is gone. A catalog created from a channel has no request in its evidence yet.
    public async Task<FormCatalogAssistantImportPending?> FindPendingAsync(Guid actor, FormCatalogStoredRow catalog, CancellationToken cancellationToken)
    {
        if (catalog is not { Kind: "Manual", ProposalState: BusinessFormCatalogProposalState.Pending, ItemsCount: 0, ProposalId: { } proposalId,
                ProposalEvidenceJson: { } evidenceJson }) return null;
        var evidence = FormCatalogProposalPresentation.Evidence(evidenceJson);
        if (evidence.RequestedByUserId != actor || evidence.RequestId is not { } requestId) return null;
        var source = await FindAsync(actor, catalog, requestId, proposalId, cancellationToken);
        return source.IsFailure ? null : new(requestId, proposalId, source.Value.Summary.FileName, source.Value.Summary.RowCount);
    }

    // The import's headers for what a proposal maps, in its order, as an upload of the template would have them: the code, the option,
    // the value and the detail, and each field by the key fieldKey gives. A field that is no longer active keeps its own header, so the
    // import warns it as a column that is not the catalog's, as it would for an uploaded file; a key the catalog never had goes as is.
    public static IReadOnlyList<Column> Columns(FormAuthoringSpreadsheetSummary summary, BusinessFormCatalogValueType valueType, string culture,
        IReadOnlyList<BusinessFormCatalogField> fields, Func<FormAuthoringMappedColumn, string?> fieldKey)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(fieldKey);
        var headers = FormCatalogImportTemplate.Columns(valueType, culture, culture, fields, translation: false)
            .ToDictionary(column => column.Key, column => column.Header, StringComparer.Ordinal);
        string? Field(FormAuthoringMappedColumn column) => fieldKey(column) is { } key
            ? fields.FirstOrDefault(field => field.Key == key) is { } field ? FormCatalogImportTemplate.FieldHeader(field, culture, false) : key
            : null;
        return summary.Columns.Select(column => (column.Letter, Header: column.Target switch
            {
                "code" => headers.GetValueOrDefault("code"), "option" => headers.GetValueOrDefault("text"),
                "value" => headers.GetValueOrDefault("value"), "detail" => headers.GetValueOrDefault("detail"), _ => Field(column),
            }))
            .Where(column => column.Header is not null).Select(column => new Column(column.Letter, column.Header!)).ToArray();
    }

    // The sheet the import reads: a row of headers, then the rows of data with each cell in its new column, keeping its text, its
    // error and whether it is canonical. With headers, the header row is the first row with content, as the prepared sheet the model
    // saw: an empty row or a title on top is skipped the same way. The SHA-256 is the copy's.
    public static FormSpreadsheet Sheet(FormSpreadsheet sheet, bool hasHeaders, IReadOnlyList<Column> columns)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(columns);
        var rows = sheet.Rows.Where(FormSpreadsheetContentPolicy.HasContent).ToList();
        var header = hasHeaders && rows.Count > 0 ? rows[0] : null;
        var indexes = columns.Select(column => FormAuthoringSpreadsheetPolicy.Number(column.Letter) is { } number ? number - 1 : -1).ToArray();
        FormSpreadsheetRow Map(FormSpreadsheetRow row) => new(row.Number,
            indexes.Select(index => row.Cells.ElementAtOrDefault(index) ?? new FormSpreadsheetCell(null)).ToArray());
        return new(sheet.Name, sheet.Sha256,
            [new(header?.Number ?? 0, columns.Select(column => new FormSpreadsheetCell(column.Header)).ToArray()), .. rows.Skip(header is null ? 0 : 1).Select(Map)]);
    }

    private async Task<Result<Source>> FindAsync(Guid actor, FormCatalogStoredRow catalog, Guid requestId, Guid proposalId, CancellationToken cancellationToken)
    {
        var found = await lookup.FindAsync(actor, requestId, proposalId, BusinessFormAuthoringAssistantCapabilitySource.CatalogGroup, cancellationToken);
        if (found.IsFailure || found.Value.Attachment is not { } copy) return FormCatalogErrors.ProposalUnavailable;
        switch (found.Value.Proposal)
        {
            // The catalog created from it, which need not be the latest: it names its fields, created with the keys the editor makes.
            case { Capability: { Id: BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, Version: BusinessFormAuthoringAssistantCapabilitySource.MappedCatalogVersion },
                    Payload: FormAuthoringMappedCatalogProposal { Spreadsheet: { } summary } mapped } when catalog.ProposalId == proposalId:
                var keys = mapped.Fields.Zip(FormAuthoringProposalPolicy.CatalogFields(mapped.ToCatalog()))
                    .ToDictionary(pair => pair.First.Name, pair => pair.Second.Key, StringComparer.Ordinal);
                return new Source(summary, copy, column => column.Field is { } name ? keys.GetValueOrDefault(name) : null);
            // An import into this catalog, while no later proposal replaced it: its fields come with their keys.
            case { Capability: { Id: BusinessFormAuthoringAssistantCapabilitySource.ProposeImportId, Version: BusinessFormAuthoringAssistantCapabilitySource.Version },
                    Payload: FormAuthoringImportProposal import } when import.Catalog.Id == catalog.Id:
                return found.Value.IsCurrent ? new Source(import.Spreadsheet, copy, column => column.Key) : FormCatalogErrors.ProposalReplaced;
            default:
                return FormCatalogErrors.ProposalUnavailable;
        }
    }

    // The copy of the turn's spreadsheet, only if it is the file the proposal mapped: exactly its size and its SHA-256.
    private async Task<Result<byte[]>> OpenAsync(AssistantAttachmentDescriptor copy, CancellationToken cancellationToken)
    {
        if (!files.IsAvailable || copy.Size is < 1 or > FormCatalogImportLimits.MaximumFileBytes) return FormCatalogImportErrors.SheetUnavailable;
        await using var stream = await files.OpenReadAsync(copy.StorageKey, cancellationToken);
        if (stream is null) return FormCatalogImportErrors.SheetUnavailable;
        var content = new byte[copy.Size];
        var read = 0;
        while (read < content.Length && await stream.ReadAsync(content.AsMemory(read), cancellationToken) is var count and > 0) read += count;
        if (read != content.Length || await stream.ReadAsync(new byte[1], cancellationToken) != 0) return FormCatalogImportErrors.SheetUnavailable;
        return string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), copy.Sha256, StringComparison.Ordinal)
            ? content : FormCatalogImportErrors.SheetUnavailable;
    }
}
