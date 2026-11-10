using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormRuleOptionsPolicy(IFormRuleReader reader, FormConditionChoicePolicy choiceOptions, IEnumerable<IStructuredRecordIssuer> issuers)
{
    public async Task<FormRuleOptionsResponse?> ReadAsync(Guid actor, Guid definitionId, CancellationToken cancellationToken)
    {
        var definition = await reader.FindDefinitionAsync(definitionId, cancellationToken);
        if (definition is null) return null;
        var draft = FormDraftPolicy.Read<FormQuestionSchema>(definition.SchemaJson);
        var questions = draft.Fields.Concat(definition.HistoricalSchemas.OrderByDescending(version => version.Number)
            .SelectMany(version => FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson).Questions)).DistinctBy(field => field.FieldId).ToArray();
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
        var choices = await choiceOptions.ReadAsync(actor,questions,culture,cancellationToken);
        return new FormRuleOptionsResponse(definition.Id, FormDraftPolicy.Read<Dictionary<string,string>>(definition.NameJson), definition.OriginalCulture,
            questions, await reader.ListRolesAsync(cancellationToken)) { ConditionChoices = choices, Issuers = Issuers(definition) };
    }
    // An issuer is offered only on a form copied from its standard and carrying its bound data.
    private FormRuleIssuerResponse[]? Issuers(FormRuleDefinitionRow definition)
    {
        if (definition.StandardKey is not { } key || definition.StandardBindingsJson is null) return null;
        var available = issuers.Where(issuer => issuer.StandardKey == key)
            .Select(issuer => new FormRuleIssuerResponse(issuer.IssuerId, issuer.Name, issuer.ResultFields)).ToArray();
        return available.Length == 0 ? null : available;
    }
}
