using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Whether loading a form in the app ends in «¿La emito?» (EM2-A): it was copied from the standard of an installed issuer and
/// its current rules issue with it. Gives the issuer, the bound data and the culture of the form.
/// </summary>
internal sealed class FormIssueEntryPolicy(IEnumerable<IStructuredRecordIssuer> issuers, IFormRuleReader rules)
{
    public async Task<FormIssueEntry?> FindAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        if (!issuers.Any()) return null;
        var definition = await rules.FindDefinitionAsync(definitionId, cancellationToken);
        if (definition?.StandardKey is not { } key || definition.StandardBindingsJson is not { } bindings) return null;
        if (issuers.FirstOrDefault(value => value.StandardKey == key) is not { } issuer) return null;
        var current = await rules.FindCurrentAsync(definitionId, cancellationToken);
        if (current is null || !FormDraftPolicy.Read<FormRule[]>(current.RulesJson)
                .Any(rule => rule.Actions.Any(action => action.Kind == "IssueDocument" && action.IssuerId == issuer.IssuerId))) return null;
        return new(issuer, FormDraftPolicy.Read<Dictionary<string, Guid>>(bindings), definition.OriginalCulture);
    }
}

internal sealed record FormIssueEntry(IStructuredRecordIssuer Issuer, IReadOnlyDictionary<string, Guid> Bound, string Culture);
