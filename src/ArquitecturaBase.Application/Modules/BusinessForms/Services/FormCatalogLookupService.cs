using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The registry is asked outside any UoW; <see cref="FormCatalogPartyValuesPolicy"/> puts its values in the active fields of the
/// catalog, as for the party an issuer completed.
/// </summary>
internal sealed class FormCatalogLookupService(FormCatalogAccessGuard access, IFormCatalogReader reader, FormCatalogPartyValuesPolicy partyValues,
    IEnumerable<IStructuredPartyLookup> lookups, IRequestValidator validator, ILogger<FormCatalogLookupService> logger) : IFormCatalogLookupService
{
    public Task<Result<FormCatalogLookupResponse>> GetAsync(Guid catalogId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormCatalogLookupResponse>>(logger, "GetFormCatalogLookup", async () =>
        {
            var found = await FindAsync(catalogId, cancellationToken);
            return found.IsFailure ? found.Error : new FormCatalogLookupResponse(found.Value.Lookup.Name, found.Value.TaxId.Id);
        });

    public Task<Result<FormCatalogLookupResultResponse>> LookupAsync(Guid catalogId, LookupFormCatalogPartyRequest request,
        CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormCatalogLookupResultResponse>>(logger, "LookupFormCatalogParty", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var found = await FindAsync(catalogId, cancellationToken);
            if (found.IsFailure) return found.Error;
            var party = await found.Value.Lookup.FindAsync(request.TaxId!.Trim(), cancellationToken);
            if (party.FailureCode is { } code) return new FormCatalogLookupResultResponse(null, [], code, ErrorMessages.Get(code));
            var values = await partyValues.MapAsync(found.Value.Fields, party.Fields, cancellationToken);
            return new FormCatalogLookupResultResponse(party.Name, values.Select(mapped => new FormCatalogLookupValueResponse(mapped.Field.Id, mapped.Value)).ToArray());
        });

    private async Task<Result<(IStructuredPartyLookup Lookup, BusinessFormCatalogField TaxId, IReadOnlyList<BusinessFormCatalogField> Fields)>> FindAsync(
        Guid catalogId, CancellationToken cancellationToken)
    {
        var allowed = await access.CheckAsync(true, cancellationToken);
        if (allowed.IsFailure) return allowed.Error;
        var row = await reader.FindAsync(catalogId, cancellationToken);
        if (row is null) return FormCatalogErrors.NotFound;
        var fields = FormCatalogFieldPolicy.ReadFields(row.FieldsJson).Where(field => field.IsActive).ToArray();
        if (lookups.FirstOrDefault(lookup => lookup.CatalogKey == row.Key) is not { } found
            || fields.FirstOrDefault(field => field.Key == found.TaxIdField) is not { } taxId) return FormCatalogErrors.LookupUnavailable;
        return (found, taxId, fields);
    }
}
