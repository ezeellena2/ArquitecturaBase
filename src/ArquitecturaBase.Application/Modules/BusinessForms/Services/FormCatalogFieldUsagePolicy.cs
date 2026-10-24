using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The forms that use a catalog field are named only to actors who can read forms (formtemplates.read). Without it each use
// keeps only its stage, once per form and stage (Active before Draft), so the block and the count stay visible without
// naming the form, its question or their identities. The catalog's own presentation use is not a form use and is added by
// the caller as it is.
internal static class FormCatalogFieldUsagePolicy
{
    public static FormCatalogFieldUsageResponse[] Describe(IEnumerable<FormCatalogFieldUsageRow> rows, bool canReadForms) => canReadForms
        ? rows.Select(use => new FormCatalogFieldUsageResponse(use.DefinitionId, use.FormName, use.Question, use.QuestionId, use.Stage)).ToArray()
        : rows.DistinctBy(use => (use.DefinitionId, use.Stage)).OrderBy(use => use.Stage == "Active" ? 0 : 1)
            .Select(use => new FormCatalogFieldUsageResponse(null, null, null, null, use.Stage)).ToArray();
}
