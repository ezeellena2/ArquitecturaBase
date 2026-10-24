using System.Globalization;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The most options one person can get from a question's filters, without a person or a saved form: the question editor
// shows it while the filters are being chosen. Same permissions as the person preview.
internal sealed class FormRecipientMaximumService(FormDraftAccessGuard access, IRequestValidator validator, IFormCatalogReader catalogs,
    IFormPersonDataReader people, FormCatalogRecipientMaximumPolicy maximums) : IFormRecipientMaximumService
{
    public async Task<Result<FormRecipientMaximumResponse>> FindMaximumAsync(FormRecipientMaximumRequest request, CancellationToken ct)
    {
        var allowed = await access.CheckRecipientPreviewAsync(ct);
        if (allowed.IsFailure) return allowed.Error;
        if (await validator.ValidateAsync(request, ct) is { } invalid) return invalid;
        var catalogId = request.Source.Catalog!.Value;
        var matches = request.Source.Match!;
        var catalog = await catalogs.FindAsync(catalogId, ct);
        if (catalog is not { IsActive: true, Kind: "Manual" }) return await RejectAsync(FieldErrors.On(FormPublicationErrors.CatalogInvalid, "source.catalog"), ct);
        var fields = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
        var attributes = matches.Any(match => match.PersonAttributeId is not null) ? await people.ListAttributesAsync(ct) : [];
        if (FormCatalogMatchPolicy.Validate(matches, fields, attributes) is { } mismatch) return await RejectAsync(FieldErrors.On(mismatch, "source.match"), ct);
        var uiCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
        var (maximum, label) = await maximums.FindAsync(catalogId, fields, matches, uiCulture, ct);
        allowed = await access.CheckRecipientPreviewAsync(ct);
        return allowed.IsFailure ? allowed.Error : new FormRecipientMaximumResponse(maximum, label);
    }

    // An error found after reading describes the catalog and its setup: like the preview's absences, it is returned only
    // while the permissions still hold.
    private async Task<Result<FormRecipientMaximumResponse>> RejectAsync(Error error, CancellationToken ct)
    {
        var allowed = await access.CheckRecipientPreviewAsync(ct);
        return allowed.IsFailure ? allowed.Error : error;
    }
}
