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
            if (standard.Catalogs is { Count: > 0 } named)
            {
                var catalogs = await _reader.ListCatalogIdsByKeyAsync(named.Values.Distinct(StringComparer.Ordinal).ToArray(), cancellationToken);
                if (named.Values.Any(catalogKey => !catalogs.ContainsKey(catalogKey))) return FormLibraryErrors.CatalogUnavailable;
                draft = draft with { Fields = draft.Fields.Select(field => named.TryGetValue(field.LocalId, out var catalogKey)
                    ? field with { Source = (field.Source ?? new FormQuestionSource(null)) with { Catalog = catalogs[catalogKey] } } : field).ToArray() };
            }
            return await _drafts.CreateAsync(new SaveFormDraftRequest(request.RequestId, request.Name, request.Prefix, draft)
                { Standard = new FormDraftStandardOrigin(standard.Key, standard.Version, standard.Bindings) }, cancellationToken);
        });
}
