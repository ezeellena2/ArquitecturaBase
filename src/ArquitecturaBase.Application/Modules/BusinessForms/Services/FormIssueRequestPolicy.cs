using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What an issuer receives for a response: the bound data of its standard (<see cref="FormIssueDataPolicy"/>), with the codes of
/// the catalog options it chose, and the language of its form. Null when the form was not copied from the standard of the issuer.
/// Only reads; the issuance and its copy by email share it.
/// </summary>
internal sealed class FormIssueRequestPolicy(IFormResponseReader records, IFormRuleReader definitions, IFormCatalogItemCodeReader codes)
{
    public async Task<(StructuredIssueRequest Request, string Culture)?> RequestAsync(Guid runId, Guid recordId, IStructuredRecordIssuer issuer,
        CancellationToken cancellationToken)
    {
        var record = await records.FindRecordAsync(recordId, cancellationToken)
            ?? throw new InvalidOperationException("The action response is missing.");
        var definition = await definitions.FindDefinitionAsync(record.Record.DefinitionId, cancellationToken);
        if (definition?.StandardKey != issuer.StandardKey || definition.StandardBindingsJson is not { } bindings) return null;
        var bound = FormDraftPolicy.Read<Dictionary<string, Guid>>(bindings);
        var itemCodes = await codes.ListAsync(FormIssueDataPolicy.ReferencedItemIds(record.Record), cancellationToken);
        return (new(runId, recordId, FormIssueDataPolicy.Read(record.Record, bound, definition.OriginalCulture, itemCodes)), definition.OriginalCulture);
    }
}
