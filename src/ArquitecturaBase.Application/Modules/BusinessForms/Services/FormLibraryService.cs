using ArquitecturaBase.Application.Common.Logging;
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
/// names its catalogs by Key: one that is missing or inactive stops the copy, and so does an option code («code:other») that a condition
/// names, or that it declares as the option a question starts with (IV1-A), and the catalog does not have among its active options.
/// The catalogs it brings come with their first options. The copy and the catalogs it creates go, in the same save, to the folder
/// chosen or else to the standard's (<see cref="FormSystemFolderLinker"/>), and the rules it brings are the first revision of the
/// copy's rules (<see cref="FormStandardRuleRecorder"/>). DOC1: a standard that reads documents says what it reads and its fixed
/// validations, the copy starts with its reading on, and the standards of the next stages are listed as «Próximamente»; one with a
/// photo or document question is offered only with private storage (<see cref="FormLibraryOfferPolicy"/>).
/// </summary>
internal sealed class FormLibraryService : IFormLibraryService
{
    private readonly FormLibraryOfferPolicy _library;
    private readonly IFormDraftService _drafts;
    private readonly IFormLibraryReader _reader;
    private readonly FormDraftAccessGuard _access;
    private readonly FormStandardRuleRecorder _rules;
    private readonly FormStandardCopyRecorder _copies;
    private readonly IFormCatalogItemCodeReader _codes;
    private readonly ILogger<FormLibraryService> _logger;

    public FormLibraryService(FormLibraryOfferPolicy library, IFormDraftService drafts, IFormLibraryReader reader,
        FormDraftAccessGuard access, FormStandardRuleRecorder rules, FormStandardCopyRecorder copies, IFormCatalogItemCodeReader codes,
        ILogger<FormLibraryService> logger) =>
        (_library, _drafts, _reader, _access, _rules, _copies, _codes, _logger) = (library, drafts, reader, access, rules, copies, codes, logger);

    public Task<Result<FormLibraryResponse>> ListAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormLibraryResponse>>(_logger, "ListFormLibrary", async () =>
        {
            var access = await _access.CheckAsync(false, cancellationToken); if (access.IsFailure) return access.Error;
            var standards = _library.Offered();
            var copies = standards.Count == 0 ? new Dictionary<string, int>()
                : await _reader.ListCopyCountsAsync(standards.Select(standard => standard.Key).ToArray(), cancellationToken);
            // The number takes at most three characters of the twelve, so the prefixes in use start with the stem up to nine.
            var taken = standards.Count == 0 ? new HashSet<string>()
                : await _reader.ListPrefixesAsync(standards.Select(standard => standard.Prefix[..Math.Min(standard.Prefix.Length, 9)]).ToArray(), cancellationToken);
            var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
            return new FormLibraryResponse(standards.OrderBy(standard => FormRecordPresentationPolicy.Text(standard.Name, culture), StringComparer.CurrentCultureIgnoreCase)
                .Select(standard => new FormLibraryStandardResponse(standard.Key, standard.Version, FormRecordPresentationPolicy.Text(standard.Name, culture),
                    FormRecordPresentationPolicy.Text(standard.Description, culture), standard.Prefix, copies.GetValueOrDefault(standard.Key),
                    FormDraftPolicy.Read<FormDraftInput>(standard.DraftJson).Culture, FormStandardPolicy.SuggestedPrefix(standard.Prefix, taken))
                    {
                        Source = standard.Source is { } source ? FormRecordPresentationPolicy.Text(source, culture) : null,
                        Reads = FormStandardPolicy.Reads(standard, culture), Validations = FormStandardPolicy.Validations(standard, culture),
                    }).ToArray())
            {
                Upcoming = _library.Upcoming.Count == 0 ? null : _library.Upcoming.Select(standard => new FormLibraryUpcomingResponse(standard.Key,
                    FormRecordPresentationPolicy.Text(standard.Name, culture), FormRecordPresentationPolicy.Text(standard.Description, culture))).ToArray(),
            };
        });

    public Task<Result<FormDraftCommitResponse>> AddAsync(string key, AddFormStandardRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormDraftCommitResponse>>(_logger, "AddFormStandard", async () =>
        {
            var access = await _access.CheckAsync(true, cancellationToken); if (access.IsFailure) return access.Error;
            if (_library.Offered().SingleOrDefault(standard => standard.Key == key) is not { } standard) return FormLibraryErrors.StandardNotFound;
            var draft = FormDraftPolicy.Read<FormDraftInput>(standard.DraftJson);
            // DOC1: a standard that reads documents leaves the copy reading them, in the copy's language; its validations come in its questions.
            draft = draft with { DocumentReading = FormStandardPolicy.Reading(standard, draft.Culture) };
            var named = standard.Catalogs ?? new Dictionary<string, string>();
            var own = standard.OwnCatalogs ?? [];
            var keys = named.Values.Concat(own.Select(catalog => catalog.Key))
                .Concat(own.SelectMany(catalog => catalog.Fields).Where(field => field.SourceCatalog is not null).Select(field => field.SourceCatalog!))
                .Distinct(StringComparer.Ordinal).ToArray();
            var existing = keys.Length == 0 ? new Dictionary<string, FormLibraryCatalogRow>() : await _reader.ListCatalogsByKeyAsync(keys, cancellationToken);
            var ids = existing.Where(pair => pair.Value.IsActive).ToDictionary(pair => pair.Key, pair => pair.Value.Id, StringComparer.Ordinal);
            // The catalogs the standard brings that do not exist yet are created with the draft; one that exists inactive stops it.
            var created = new List<BusinessFormCatalog>();
            var createdItems = new List<BusinessFormCatalogItem>();
            var createdCodes = new Dictionary<Guid, IReadOnlyDictionary<string, Guid>>();
            foreach (var catalog in own.Where(catalog => !existing.ContainsKey(catalog.Key)))
            {
                if (catalog.Fields.Any(field => field.SourceCatalog is { } source && !ids.ContainsKey(source))) return FormLibraryErrors.CatalogUnavailable;
                var entity = BusinessFormCatalog.CreateManual(catalog.OriginalCulture, catalog.Name, catalog.Description);
                entity.AssignStandardKey(catalog.Key);
                var fields = entity.SetFields(catalog.Fields.Select((field, position) => new BusinessFormCatalogField(BusinessFormCatalogField.CreateIdentity(),
                    field.Key, field.Name, field.Type, field.SourceCatalog is { } source ? ids[source] : null, false, false, null, null, field.MaxLength,
                    "data", position, true)).ToArray(), null);
                if (fields.IsFailure) throw new InvalidOperationException("A library standard declares invalid catalog fields.");
                var items = (catalog.Items ?? []).Select((item, position) => entity.AddManualItem(item.Code, item.Text.GetValueOrDefault("es"),
                    item.Text.GetValueOrDefault("en"), item.Detail?.GetValueOrDefault("es"), item.Detail?.GetValueOrDefault("en"), position)).ToArray();
                if (items.Any(item => item.IsFailure)) throw new InvalidOperationException("A library standard declares invalid catalog options.");
                createdItems.AddRange(items.Select(item => item.Value));
                createdCodes[entity.Id] = items.ToDictionary(item => item.Value.Code, item => item.Value.Id, StringComparer.Ordinal);
                created.Add(entity);
                ids[catalog.Key] = entity.Id;
            }
            if (named.Values.Any(catalogKey => !ids.ContainsKey(catalogKey))) return FormLibraryErrors.CatalogUnavailable;
            draft = draft with { Fields = draft.Fields.Select(field => named.TryGetValue(field.LocalId, out var catalogKey)
                ? field with { Source = (field.Source ?? new FormQuestionSource(null)) with { Catalog = ids[catalogKey] } } : field).ToArray() };
            var coded = await NameOptionsAsync(draft, named, standard.Defaults ?? new Dictionary<string, string>(), ids, createdCodes, cancellationToken);
            if (coded.IsFailure) return coded.Error;
            var rules = await _rules.PrepareAsync(standard, named, ids, createdCodes, cancellationToken);
            if (rules.IsFailure) return rules.Error;
            return await _drafts.CreateAsync(new SaveFormDraftRequest(request.RequestId, request.Name, request.Prefix, coded.Value)
            {
                Standard = new FormDraftStandardOrigin(standard.Key, standard.Version, standard.Bindings)
                    { RecordAsync = _copies.Prepare(created, createdItems, rules.Value, request.FolderId, standard.Folder) },
            }, cancellationToken);
        });

    // A condition on a catalog question names the options by «code:<code>», and the option a question starts with (IV1-A) is declared
    // by its code: the copy takes the identity of the active option with that code in this installation, also in a catalog created in
    // this same save. A code the catalog lacks stops the copy.
    private async Task<Result<FormDraftInput>> NameOptionsAsync(FormDraftInput draft, IReadOnlyDictionary<string, string> named,
        IReadOnlyDictionary<string, string> defaults, Dictionary<string, Guid> catalogIds, Dictionary<Guid, IReadOnlyDictionary<string, Guid>> createdCodes,
        CancellationToken cancellationToken)
    {
        const string prefix = "code:";
        var fields = draft.Fields.ToArray();
        // Which catalog each condition and each starting option names, and their codes, so each catalog is read once.
        var conditions = new Dictionary<int, Guid>();
        var starting = new Dictionary<int, (Guid CatalogId, string Code)>();
        var wanted = new Dictionary<Guid, HashSet<string>>();
        void Want(Guid catalogId, IEnumerable<string> codes)
        {
            if (!wanted.TryGetValue(catalogId, out var set)) wanted[catalogId] = set = new HashSet<string>(StringComparer.Ordinal);
            set.UnionWith(codes);
        }
        for (var index = 0; index < fields.Length; index++)
        {
            if (fields[index].ShowWhen is { } showWhen && showWhen.Values.Any(value => value.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var catalogKey = named.Where(pair => Guid.TryParse(pair.Key, out var localId) && localId == showWhen.QuestionId).Select(pair => pair.Value).FirstOrDefault()
                    ?? throw new InvalidOperationException("A library standard names an option code outside a catalog question.");
                conditions[index] = catalogIds[catalogKey];
                Want(catalogIds[catalogKey], showWhen.Values.Where(value => value.StartsWith(prefix, StringComparison.Ordinal)).Select(value => value[prefix.Length..]));
            }
            if (defaults.TryGetValue(fields[index].LocalId, out var code))
            {
                var catalogKey = named.GetValueOrDefault(fields[index].LocalId)
                    ?? throw new InvalidOperationException("A library standard declares a starting option outside a catalog question.");
                starting[index] = (catalogIds[catalogKey], code);
                Want(catalogIds[catalogKey], [code]);
            }
        }
        var options = new Dictionary<Guid, IReadOnlyDictionary<string, Guid>>();
        foreach (var (catalogId, codes) in wanted)
        {
            options[catalogId] = createdCodes.TryGetValue(catalogId, out var own) ? own : await _codes.ListActiveAsync(catalogId, codes, cancellationToken);
            if (codes.Any(code => !options[catalogId].ContainsKey(code))) return FormLibraryErrors.CatalogUnavailable;
        }
        foreach (var (index, catalogId) in conditions)
        {
            var showWhen = fields[index].ShowWhen!;
            fields[index] = fields[index] with { ShowWhen = showWhen with { Values = showWhen.Values
                .Select(value => value.StartsWith(prefix, StringComparison.Ordinal) ? options[catalogId][value[prefix.Length..]].ToString("D") : value).ToArray() } };
        }
        foreach (var (index, (catalogId, code)) in starting)
            fields[index] = fields[index] with { Source = fields[index].Source! with { Default = options[catalogId][code] } };
        return draft with { Fields = fields };
    }

}
