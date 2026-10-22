using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogImportTemplate
{
    public static IReadOnlyList<FormCatalogImportTemplateColumn> Columns(BusinessFormCatalogValueType valueType, string culture, bool canonicalValue = false)
    {
        var columns = new List<FormCatalogImportTemplateColumn>
        {
            new("code", FormCatalogTexts.Get("HeaderCode", culture), ""),
            new("text", FormCatalogTexts.Get("HeaderText", culture), FormCatalogTexts.Get("RuleText", culture)),
            new("textEn", FormCatalogTexts.Get("HeaderTextEn", culture), FormCatalogTexts.Get("RuleTextEn", culture)),
            new("detail", FormCatalogTexts.Get("HeaderDetail", culture), FormCatalogTexts.Get("RuleDetail", culture)),
            new("detailEn", FormCatalogTexts.Get("HeaderDetailEn", culture), FormCatalogTexts.Get("RuleDetail", culture)),
            new("active", FormCatalogTexts.Get("HeaderActive", culture), FormCatalogTexts.Get("RuleActive", culture)),
        };
        if (valueType != BusinessFormCatalogValueType.Text)
            columns.Add(new("value", ValueHeader(valueType, culture, canonicalValue), FormCatalogTexts.Get("RuleValue" + valueType, culture)));
        return columns;
    }

    public static string ValueHeader(BusinessFormCatalogValueType valueType, string culture, bool canonical) =>
        FormCatalogTexts.Get((canonical ? "HeaderCanonicalValue" : "HeaderValue") + valueType, culture);
}
