using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The library (BI1-A, as DOC1 without reading documents). It lists the standards of the installed modules and «Agregar» creates
/// an own draft through the same save as any other, which seals the standard, its version and its bound questions. A standard
/// names its catalogs by Key: one that is missing or inactive stops the copy.
/// </summary>
internal sealed class FormLibraryService : IFormLibraryService
{
    private readonly ArquitecturaBase.Application.Models.StructuredData.StructuredFormStandard[] _standards;
    private readonly IFormDraftService _drafts;
    private readonly IFormLibraryReader _reader;
    private readonly FormDraftAccessGuard _access;
    private readonly ILogger<FormLibraryService> _logger;

    public FormLibraryService(IEnumerable<IStructuredFormStandardSource> sources, IFormDraftService drafts, IFormLibraryReader reader,
        FormDraftAccessGuard access, ILogger<FormLibraryService> logger)
    {
        _standards = sources.SelectMany(source => source.Standards).ToArray();
        if (_standards.DistinctBy(standard => standard.Key, StringComparer.Ordinal).Count() != _standards.Length)
            throw new InvalidOperationException("Library standard keys must be unique.");
        (_drafts, _reader, _access, _logger) = (drafts, reader, access, logger);
    }

    public Task<Result<FormLibraryResponse>> ListAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormLibraryResponse>>(_logger, "ListFormLibrary", async () =>
        {
            var access = await _access.CheckAsync(false, cancellationToken); if (access.IsFailure) return access.Error;
            var copies = _standards.Length == 0 ? new Dictionary<string, int>()
                : await _reader.ListCopyCountsAsync(_standards.Select(standard => standard.Key).ToArray(), cancellationToken);
            var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
            return new FormLibraryResponse(_standards.OrderBy(standard => FormRecordPresentationPolicy.Text(standard.Name, culture), StringComparer.CurrentCultureIgnoreCase)
                .Select(standard => new FormLibraryStandardResponse(standard.Key, standard.Version, FormRecordPresentationPolicy.Text(standard.Name, culture),
                    FormRecordPresentationPolicy.Text(standard.Description, culture), standard.Prefix, copies.GetValueOrDefault(standard.Key))).ToArray());
        });

    public Task<Result<FormDraftCommitResponse>> AddAsync(string key, AddFormStandardRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormDraftCommitResponse>>(_logger, "AddFormStandard", async () =>
        {
            var access = await _access.CheckAsync(true, cancellationToken); if (access.IsFailure) return access.Error;
            if (_standards.SingleOrDefault(standard => standard.Key == key) is not { } standard) return FormLibraryErrors.StandardNotFound;
            var draft = FormDraftPolicy.Read<FormDraftInput>(standard.DraftJson);
            var named = standard.Catalogs ?? new Dictionary<string, string>();
            var own = standard.OwnCatalogs ?? [];
            var keys = named.Values.Concat(own.Select(catalog => catalog.Key))
                .Concat(own.SelectMany(catalog => catalog.Fields).Where(field => field.SourceCatalog is not null).Select(field => field.SourceCatalog!))
                .Distinct(StringComparer.Ordinal).ToArray();
            var existing = keys.Length == 0 ? new Dictionary<string, FormLibraryCatalogRow>() : await _reader.ListCatalogsByKeyAsync(keys, cancellationToken);
            var ids = existing.Where(pair => pair.Value.IsActive).ToDictionary(pair => pair.Key, pair => pair.Value.Id, StringComparer.Ordinal);
            // The catalogs the standard brings that do not exist yet are created with the draft; one that exists inactive stops it.
            var created = new List<BusinessFormCatalog>();
            foreach (var catalog in own.Where(catalog => !existing.ContainsKey(catalog.Key)))
            {
                if (catalog.Fields.Any(field => field.SourceCatalog is { } source && !ids.ContainsKey(source))) return FormLibraryErrors.CatalogUnavailable;
                var entity = BusinessFormCatalog.CreateManual(catalog.OriginalCulture, catalog.Name, catalog.Description);
                entity.AssignStandardKey(catalog.Key);
                var fields = entity.SetFields(catalog.Fields.Select((field, position) => new BusinessFormCatalogField(BusinessFormCatalogField.CreateIdentity(),
                    field.Key, field.Name, field.Type, field.SourceCatalog is { } source ? ids[source] : null, false, false, null, null, field.MaxLength,
                    "data", position, true)).ToArray(), null);
                if (fields.IsFailure) throw new InvalidOperationException("A library standard declares invalid catalog fields.");
                created.Add(entity);
                ids[catalog.Key] = entity.Id;
            }
            if (named.Values.Any(catalogKey => !ids.ContainsKey(catalogKey))) return FormLibraryErrors.CatalogUnavailable;
            draft = draft with { Fields = draft.Fields.Select(field => named.TryGetValue(field.LocalId, out var catalogKey)
                ? field with { Source = (field.Source ?? new FormQuestionSource(null)) with { Catalog = ids[catalogKey] } } : field).ToArray() };
            return await _drafts.CreateAsync(new SaveFormDraftRequest(request.RequestId, request.Name, request.Prefix, draft)
                { Standard = new FormDraftStandardOrigin(standard.Key, standard.Version, standard.Bindings) { NewCatalogs = created } }, cancellationToken);
        });
}
