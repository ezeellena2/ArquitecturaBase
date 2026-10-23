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
        var fields = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
        var columns = FormCatalogImportTemplate.Columns(Enum.Parse<BusinessFormCatalogValueType>(catalog.ValueType), culture,
            canonicalValue: !template, fields: fields);
        IReadOnlyList<IReadOnlyList<FormWorkbookCell>> rows;
        if (template) rows = [columns.Select(column => new FormWorkbookCell("text", column.Rule)).ToArray()];
        else
        {
            var items = await reader.ListAllItemsAsync(id, cancellationToken);
            if (!await access.HasPermissionAsync(actor.Value, BusinessFormsPermissions.CatalogsRead, cancellationToken))
                return FormDefinitionErrors.Forbidden;
            rows = items.Select(item =>
            {
                var values = FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson);
                return (IReadOnlyList<FormWorkbookCell>)columns.Select(column => new FormWorkbookCell("text", column.Key switch
                {
                    "code" => item.Code, "text" => item.TextEs, "textEn" => item.TextEn,
                    "detail" => item.DetailEs, "detailEn" => item.DetailEn,
                    "active" => FormCatalogTexts.Get(item.IsActive ? "Yes" : "No", culture), "value" => item.Value,
                    _ => values.TryGetValue(column.Key, out var value) ? FormCatalogFieldPolicy.Input(value) : null,
                })).ToArray();
            }).ToArray();
        }
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = writer.Write(new(columns.Select(column => column.Header).ToArray(), rows, FormCatalogTexts.Get("Sheet", culture)), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await access.HasPermissionAsync(actor.Value, BusinessFormsPermissions.CatalogsRead, cancellationToken))
            return FormDefinitionErrors.Forbidden;
        return new FormExportResponse(bytes, catalog.Key + (template ? "-template" : "") + ".xlsx");
    }
}
