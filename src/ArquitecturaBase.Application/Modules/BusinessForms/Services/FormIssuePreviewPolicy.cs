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

    // The options chosen, as the capture keeps them: each with its item of the block and the fields of a manual catalog.
    private async Task<IReadOnlyList<FormRecordReferenceRow>> ReferencesAsync(FormVersionSchema schema, IReadOnlyList<FormRecordAnswer> answers,
        IReadOnlyList<StructuredFormCatalogOffer> offers, CancellationToken cancellationToken)
    {
        static IEnumerable<(FormRecordAnswer Answer, int? Item)> Placed(FormRecordAnswer answer) => answer.Rows is { } rows
            ? rows.SelectMany((items, row) => items.Select(item => (item, (int?)row)))
            : [(answer, null)];
        var selected = answers.SelectMany(Placed)
            .Where(value => schema.Questions.Single(question => question.FieldId == value.Answer.FieldId).Source?.Catalog is not null)
            .SelectMany(value => (value.Answer.ChoiceIds ?? [value.Answer.ChoiceId!.Value])
                .Select(choiceId => (value.Answer, value.Item, ChoiceId: choiceId, Offer: offers.Single(offer => offer.FieldId == value.Answer.FieldId))))
            .ToArray();
        var fields = await FormCatalogFieldCapturePolicy.CaptureAsync(records,
            selected.Where(value => FormCatalogKindPolicy.HasItems(value.Offer.Kind)).Select(value => value.ChoiceId).Distinct().ToArray(), cancellationToken);
        return selected.Select(value =>
        {
            var option = value.Offer.Options.Single(item => item.Id == value.ChoiceId);
            return new FormRecordReferenceRow(Guid.Empty, value.Answer.FieldId, value.Offer.CatalogId, value.Offer.Revision, option.Id, option.Text,
                option.Detail, value.Offer.CatalogName, FormRecordReferencePolicy.Capture(option, value.Offer), option.Value, value.Offer.ValueType,
                value.Offer.Kind == "Manual" ? fields.GetValueOrDefault(option.Id) : null, value.Item ?? 0);
        }).ToArray();
    }
}
