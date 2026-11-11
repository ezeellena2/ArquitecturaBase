using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What «Agregar» leaves in the same save as the copy of a library standard (BI1-A), once that save has the new definition: the
/// catalogs the standard brings with their first options, its rules as the first revision of the copy's, and the copy and those
/// catalogs in the folder chosen or else the standard's (<see cref="FormSystemFolderLinker"/>). The draft's save runs it through
/// <see cref="FormDraftStandardOrigin.RecordAsync"/>; it does not save on its own.
/// </summary>
internal sealed class FormStandardCopyRecorder(IFormLibraryRepository repository, FormSystemFolderLinker folders)
{
    public Func<BusinessFormDefinition, FormIdentityMap, CancellationToken, Task<Result>> Prepare(IReadOnlyList<BusinessFormCatalog> catalogs,
        IReadOnlyList<BusinessFormCatalogItem> items, Action<BusinessFormDefinition, FormIdentityMap>? rules, Guid? folderId, StructuredFormStandardFolder? folder) =>
        async (definition, identities, ct) =>
        {
            foreach (var catalog in catalogs) repository.AddCatalog(catalog);
            if (items.Count > 0) repository.AddItems(items);
            rules?.Invoke(definition, identities);
            return Filer(folderId, folder) is { } file ? await FileAsync(file, definition.Id, catalogs, ct) : Result.Success();
        };

    // The folder chosen when adding or, by default, the standard's own, created in the language of whoever adds the copy.
    private Func<FormFolderItemKind, Guid, CancellationToken, Task<Result<Guid>>>? Filer(Guid? chosen, StructuredFormStandardFolder? standard)
    {
        if (chosen is { } folderId) return (kind, id, ct) => folders.FileAsync(kind, id, folderId, ct);
        if (standard is null) return null;
        var name = FormRecordPresentationPolicy.Text(standard.Name, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es");
        return (kind, id, ct) => folders.FileAsync(kind, id, standard.Key, name, ct);
    }

    private static async Task<Result> FileAsync(Func<FormFolderItemKind, Guid, CancellationToken, Task<Result<Guid>>> file, Guid definitionId,
        IReadOnlyList<BusinessFormCatalog> catalogs, CancellationToken cancellationToken)
    {
        var filed = await file(FormFolderItemKind.Form, definitionId, cancellationToken);
        foreach (var catalog in catalogs)
        {
            if (filed.IsFailure) break;
            filed = await file(FormFolderItemKind.Catalog, catalog.Id, cancellationToken);
        }
        return filed.IsFailure ? filed.Error : Result.Success();
    }
}
