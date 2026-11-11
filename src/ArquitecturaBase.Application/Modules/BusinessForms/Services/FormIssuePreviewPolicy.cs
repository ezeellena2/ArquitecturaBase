using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «¿La emito?» before «Enviar» (EM2-A): what the issuer of the form would issue from the answers not sent yet, with the same data
/// an issuance gets (the options as the capture keeps them, by their codes) and nothing saved.
/// </summary>
internal sealed class FormIssuePreviewPolicy(FormIssueEntryPolicy entries, IFormRecordRepository records, IFormCatalogItemCodeReader codes)
{
    /// <summary>Null when the form does not issue.</summary>
    public async Task<StructuredIssuePreview?> PreviewAsync(Guid definitionId, string schemaJson, IReadOnlyList<FormRecordAnswer> answers,
        IReadOnlyList<StructuredFormCatalogOffer> offers, CancellationToken cancellationToken)
    {
        if (await entries.FindAsync(definitionId, cancellationToken) is not { } entry) return null;
        var references = await ReferencesAsync(FormDraftPolicy.Read<FormVersionSchema>(schemaJson), answers, offers, cancellationToken);
        var valuesJson = FormDraftPolicy.Serialize(answers.Select(value => new { value.FieldId, value.Value }).ToArray());
        var itemCodes = await codes.ListAsync(FormIssueDataPolicy.ReferencedItemIds(references), cancellationToken);
        return await entry.Issuer.PreviewAsync(
            FormIssueDataPolicy.Read(schemaJson, valuesJson, references, entry.Bound, entry.Culture, itemCodes), cancellationToken);
    }

    // The options chosen, built by the same piece as the capture (BusinessFormRecordRecorder), with the fields of a manual catalog
    // read the same way: the invoice confirmed is the one issued. Nothing is locked, since nothing is saved.
    private async Task<IReadOnlyList<FormRecordReferenceRow>> ReferencesAsync(FormVersionSchema schema, IReadOnlyList<FormRecordAnswer> answers,
        IReadOnlyList<StructuredFormCatalogOffer> offers, CancellationToken cancellationToken)
    {
        var chosen = FormRecordReferencePolicy.Chosen(schema, answers, offers);
        var fields = await FormCatalogFieldCapturePolicy.CaptureAsync(records, FormRecordReferencePolicy.ItemIds(chosen), cancellationToken);
        return FormRecordReferencePolicy.References(Guid.Empty, chosen, fields);
    }
}
