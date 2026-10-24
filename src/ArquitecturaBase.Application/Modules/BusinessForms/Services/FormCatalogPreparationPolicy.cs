using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogPreparationPolicy(IFormRecordRepository records, IFormCatalogRepository catalogs,
    FormCatalogOptionsPolicy options, FormPresentationCulturePolicy presentation)
{
    public async Task<StructuredFormPreparationResponse> PrepareAsync(Guid recipient, string culture,
        Guid definitionId, Guid versionId, bool forSending, CancellationToken ct)
    {
        var version = await records.GetVersionAsync(versionId, ct);
        if (version is null || version.DefinitionId != definitionId)
            return new([], [new(Guid.Empty, "", FormRecordErrors.InvalidResponse.Code)]);
        var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
        if (forSending)
            foreach (var id in schema.Questions.Where(question => question.Source?.Catalog is not null)
                .Select(question => question.Source!.Catalog!.Value).Distinct().Order())
                await catalogs.LockAsync(id, ct);
        var prepared = await options.PrepareAsync(recipient, culture, schema, version.Culture, ct, inspectOnly: true);
        if (!prepared.CanSend) return prepared;
        var localized = await presentation.PrepareAsync(definitionId, version.Culture!, culture, version.NameJson!,
            version.SubmitLabelJson!, schema, prepared, ct);
        // Shown has already been composed from catalog fields in the requested culture. Recompose it if the form falls back.
        return localized.CanSend && localized.Culture is { } effective && effective != culture
            ? await options.PrepareAsync(recipient, effective, schema, version.Culture, ct)
            : FormCatalogOptionsPolicy.ValidatePrepared(localized, schema);
    }
}
