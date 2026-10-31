using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogExportService(IFormCatalogReader reader, IFormWorkbookWriter writer,
    FormCatalogAccessGuard access) : IFormCatalogExportService
{
    public async Task<Result<FormExportResponse>> ExportAsync(Guid id, bool template, CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(false, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var catalog = await reader.FindAsync(id, cancellationToken);
        if (!await access.HasPermissionAsync(actor.Value, BusinessFormsPermissions.CatalogsRead, cancellationToken))
            return FormDefinitionErrors.Forbidden;
        if (catalog is null) return FormCatalogErrors.NotFound;
        if (catalog.Kind != "Manual") return FormCatalogErrors.ManualItemsUnavailable;
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
        var valueType = Enum.Parse<BusinessFormCatalogValueType>(catalog.ValueType);
        var fields = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
        // With the menu in the catalog's original language there are no translation columns: reimporting keeps them.
        var translation = culture != catalog.OriginalCulture;
        var columns = template ? FormCatalogImportTemplate.Columns(valueType, culture, catalog.OriginalCulture, fields, translation)
            : FormCatalogExportPolicy.Columns(valueType, culture, catalog.OriginalCulture, fields, translation);
        IReadOnlyList<IReadOnlyList<FormWorkbookCell>> rows;
        if (template) rows = [columns.Select(column => new FormWorkbookCell("text", column.Rule)).ToArray()];
        else
        {
            var items = await reader.ListAllItemsAsync(id, cancellationToken);
            // A source option goes by its text: every option of each source, also the deactivated ones.
            var sources = await FormCatalogFieldPresentation.SourcesAsync(fields, reader, cancellationToken);
            if (!await access.HasPermissionAsync(actor.Value, BusinessFormsPermissions.CatalogsRead, cancellationToken))
                return FormDefinitionErrors.Forbidden;
            rows = FormCatalogExportPolicy.Rows(columns, items, valueType, catalog.OriginalCulture, fields, sources, culture);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = writer.Write(new(columns.Select(column => column.Header).ToArray(), rows, FormCatalogTexts.Get("Sheet", culture)), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await access.HasPermissionAsync(actor.Value, BusinessFormsPermissions.CatalogsRead, cancellationToken))
            return FormDefinitionErrors.Forbidden;
        return new FormExportResponse(bytes, FileName(catalog, culture, template));
    }

    // The catalog's name in the menu's language (or the other one), without the characters a file name cannot have.
    private static string FileName(FormCatalogStoredRow catalog, string culture, bool template)
    {
        var name = FormRecordPresentationPolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson), culture);
        if (template) name = string.Format(CultureInfo.InvariantCulture, FormCatalogTexts.Get("TemplateFileName", culture), name);
        var clean = new string(name.Select(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character) ? '-' : character)
            .ToArray()).Trim(' ', '.');
        return (clean.Length == 0 ? catalog.Key : clean) + ".xlsx";
    }
}
