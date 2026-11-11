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
/// The registry is asked outside any UoW; its values are mapped to the active fields of the catalog by their keys, and a field that
/// names an option of another catalog gets the active option with the code the registry gave. A value without its field is dropped.
/// </summary>
internal sealed class FormCatalogLookupService(FormCatalogAccessGuard access, IFormCatalogReader reader, IFormCatalogItemCodeReader codes,
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
            var values = new List<FormCatalogLookupValueResponse>();
            foreach (var field in found.Value.Fields)
            {
                if (party.Fields?.GetValueOrDefault(field.Key) is not { } value) continue;
                if (field.Type != "catalog") values.Add(new(field.Id, value));
                else if (field.SourceCatalogId is { } source && await codes.FindActiveAsync(source, value, cancellationToken) is { } itemId)
                    values.Add(new(field.Id, itemId.ToString("D")));
            }
            return new FormCatalogLookupResultResponse(party.Name, values);
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
