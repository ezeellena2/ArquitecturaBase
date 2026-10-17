using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormRuleOptionsPolicy(IFormRuleReader reader, FormConditionChoicePolicy choiceOptions)
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
            questions, await reader.ListRolesAsync(cancellationToken)) { ConditionChoices = choices };
    }
}
