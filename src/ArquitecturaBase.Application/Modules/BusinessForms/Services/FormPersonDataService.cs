using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed partial class FormPersonDataService(IFormPersonDataRepository repository, IFormPersonDataReader reader,
    IFormCatalogRepository catalogs, IFormCatalogReader catalogReader, IUnitOfWork work, IRequestValidator validator,
    FormPersonDataAccessGuard access, ILogger<FormPersonDataService> logger) : IFormPersonDataService
{
    public async Task<Result<FormPersonAttributesResponse>> ListAttributesAsync(CancellationToken cancellationToken)
    {
        var allowed = await access.CheckAttributesAsync(false, cancellationToken);
        if (allowed.IsFailure) return allowed.Error;
        var attributes = await reader.ListAttributesAsync(cancellationToken);
        var uses = await reader.ListAttributeUsagesAsync(attributes.Select(row => row.Id).ToArray(), cancellationToken);
        var final = await access.CheckAttributesAsync(false, cancellationToken);
        if (final.IsFailure) return final.Error;
        return new FormPersonAttributesResponse(attributes.Select(row => Map(row, uses)).ToArray());
    }
    public async Task<Result<FormPersonAttributeResponse>> CreateAttributeAsync(CreateFormPersonAttributeRequest request, CancellationToken cancellationToken)
    {
        var allowed = await access.CheckAttributesAsync(true, cancellationToken);
        if (allowed.IsFailure) return allowed.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var result = await work.ExecuteInTransactionAsync(ct => CreateAttributeCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsSuccess) LogSaved(logger, result.Value.Id);
        return result;
    }
    public async Task<Result<FormPersonAttributeResponse>> UpdateAttributeAsync(Guid id, UpdateFormPersonAttributeRequest request, CancellationToken cancellationToken)
    {
        var allowed = await access.CheckAttributesAsync(true, cancellationToken);
        if (allowed.IsFailure) return allowed.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var result = await work.ExecuteInTransactionAsync(ct => UpdateAttributeCoreAsync(id, request, ct), CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsSuccess) LogSaved(logger, result.Value.Id);
        return result;
    }
    public async Task<Result<PagedResult<FormPersonAttributeOptionResponse>>> ListOptionsAsync(Guid id, ListFormPersonAttributeOptionsRequest request, CancellationToken cancellationToken)
    {
        var allowed = await access.CheckAttributesAsync(false, cancellationToken);
        if (allowed.IsFailure) return allowed.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var attribute = (await reader.ListAttributesAsync(cancellationToken)).SingleOrDefault(row => row.Id == id);
        if (attribute is null) return FormPersonDataErrors.AttributeNotFound;
        if (!attribute.IsActive || attribute.Type != "Catalog" || attribute.SourceCatalogId is not { } sourceId) return FormPersonDataErrors.SourceUnavailable;
        var source = await catalogReader.FindAsync(sourceId, cancellationToken);
        if (source is null || !source.IsActive || source.Kind != "Manual") return FormPersonDataErrors.SourceUnavailable;
        var options = await reader.ListOptionsAsync(sourceId, request with { DisplayCulture = Culture() }, cancellationToken);
        var final = await access.CheckAttributesAsync(false, cancellationToken);
        if (final.IsFailure) return final.Error;
        return new PagedResult<FormPersonAttributeOptionResponse>(options.Items.Select(row => new FormPersonAttributeOptionResponse(row.Id, row.Text)).ToArray(), options.Page, options.PageSize, options.TotalCount);
    }
    public async Task<Result<PagedResult<FormPersonDataTableResponse>>> ListPeopleAsync(ListFormPersonDataRequest request, CancellationToken cancellationToken)
    {
        var allowed = await access.CheckPeopleAsync(cancellationToken);
        if (allowed.IsFailure) return allowed.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var attributes = await reader.ListAttributesAsync(cancellationToken);
        var page = await reader.ListPeopleAsync(request with { DisplayCulture = Culture() }, cancellationToken);
        var final = await access.CheckPeopleAsync(cancellationToken);
        if (final.IsFailure) return final.Error;
        return new PagedResult<FormPersonDataTableResponse>(page.Items.Select(row => new FormPersonDataTableResponse(row.UserId, row.DisplayName,
            Values(row.ValuesJson, attributes, row.OptionTexts))).ToArray(), page.Page, page.PageSize, page.TotalCount);
    }
    public async Task<Result<FormPersonDataResponse>> GetDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        var allowed = await access.CheckValuesAsync(userId, cancellationToken);
        if (allowed.IsFailure) return allowed.Error;
        var row = await reader.FindRecipientDataAsync(userId, cancellationToken);
        var final = await access.CheckValuesAsync(userId, cancellationToken);
        if (final.IsFailure) return final.Error;
        return new FormPersonDataResponse(userId, row.Revision, Values(row.ValuesJson, row.Attributes, row.OptionTexts));
    }
    public async Task<Result<FormPersonDataResponse>> SaveDataAsync(Guid userId, UpdateFormPersonDataRequest request, CancellationToken cancellationToken)
    {
        var allowed = await access.CheckValuesAsync(userId, cancellationToken);
        if (allowed.IsFailure) return allowed.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var result = await work.ExecuteInTransactionAsync(ct => SaveDataCoreAsync(userId, request, ct), CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsSuccess) LogSaved(logger, userId);
        return result;
    }
    private async Task<Result<FormPersonAttributeResponse>> CreateAttributeCoreAsync(CreateFormPersonAttributeRequest request, CancellationToken ct)
    {
        await repository.LockDefinitionsAsync(ct);
        await repository.LockAttributesAsync(ct);
        var allowed = await access.CheckAttributesAsync(true, ct);
        if (allowed.IsFailure) return allowed.Error;
        var attributes = await repository.ListAttributesAsync(ct);
        if (attributes.Any(row => row.Key == request.Key)) return FieldErrors.On(FormPersonDataErrors.AttributeKeyDuplicate, "key");
        if (attributes.Count >= BusinessFormPersonAttribute.MaximumAttributes) return FormPersonDataErrors.AttributeTooMany;
        if (request.SourceCatalogId is { } sourceId) await catalogs.LockAsync(sourceId, ct);
        var source = await SourceAsync(request.Type!, request.SourceCatalogId, ct);
        if (source.IsFailure) return source.Error;
        var created = BusinessFormPersonAttribute.Create(request.Key!, Culture(), Names(request.Name!), Enum.Parse<BusinessFormPersonAttributeType>(request.Type!), request.SourceCatalogId, attributes.Count);
        if (created.IsFailure) return created.Error;
        repository.Add(created.Value);
        return Map(Row(created.Value, source.Value?.NameJson), []);
    }
    private async Task<Result<FormPersonAttributeResponse>> UpdateAttributeCoreAsync(Guid id, UpdateFormPersonAttributeRequest request, CancellationToken ct)
    {
        await repository.LockDefinitionsAsync(ct);
        await repository.LockAttributesAsync(ct);
        var allowed = await access.CheckAttributesAsync(true, ct);
        if (allowed.IsFailure) return allowed.Error;
        var attribute = await repository.GetAttributeAsync(id, ct);
        if (attribute is null) return FormPersonDataErrors.AttributeNotFound;
        if (attribute.Revision != request.ExpectedRevision) return FormPersonDataErrors.AttributeRevisionConflict;
        var uses = await reader.ListAttributeUsagesAsync([id], ct);
        if (uses.Count > 0 && (attribute.Type.ToString() != request.Type || attribute.SourceCatalogId != request.SourceCatalogId || !request.IsActive)) return FormPersonDataErrors.AttributeInUse;
        foreach (var sourceId in new[] { attribute.SourceCatalogId, request.SourceCatalogId }.OfType<Guid>().Distinct().Order()) await catalogs.LockAsync(sourceId, ct);
        var source = await SourceAsync(request.Type!, request.SourceCatalogId, ct);
        if (source.IsFailure) return source.Error;
        var typeChanged = attribute.Type.ToString() != request.Type;
        var stored = typeChanged || attribute.SourceCatalogId != request.SourceCatalogId ? await repository.ListDataWithValueAsync(id, ct) : [];
        if (stored.Count > 0 && !request.ConfirmValueRemoval)
            return FieldErrors.OnWithMetadata(FormPersonDataErrors.ValueRemovalDetails(stored.Count), typeChanged ? "type" : "sourceCatalogId");
        var updated = attribute.Update(request.ExpectedRevision!.Value, Names(request.Name!), Enum.Parse<BusinessFormPersonAttributeType>(request.Type!), request.SourceCatalogId, request.Position, request.IsActive);
        if (updated.IsFailure) return updated.Error;
        foreach (var row in stored)
        {
            var values = ReadValues(row.ValuesJson);
            values.Remove(id.ToString("D"));
            var cleared = row.Update(row.Revision, JsonSerializer.Serialize(values, JsonSerializerOptions.Web));
            if (cleared.IsFailure) return cleared.Error;
        }
        return Map(Row(attribute, source.Value?.NameJson), uses);
    }
    private async Task<Result<BusinessFormCatalog?>> SourceAsync(string type, Guid? sourceId, CancellationToken ct)
    {
        if (type == "Text") return Result.Success<BusinessFormCatalog?>(null);
        var source = await catalogs.GetAsync(sourceId!.Value, ct);
        if (source is null || !source.IsActive || source.Kind != BusinessFormCatalogKind.Manual) return FormPersonDataErrors.SourceUnavailable;
        return source;
    }
    private static FormPersonAttributeStoredRow Row(BusinessFormPersonAttribute value, string? sourceName) => new(value.Id, value.Key, value.NameJson, value.Type.ToString(), value.SourceCatalogId, value.Position, value.IsActive, value.Revision, sourceName);
    private static FormPersonAttributeResponse Map(FormPersonAttributeStoredRow row, IReadOnlyList<FormPersonAttributeUseRow> uses) => new(row.Id, row.Key, Texts(row.NameJson), row.Type, row.SourceCatalogId,
        row.SourceCatalogNameJson is null ? null : Text(row.SourceCatalogNameJson), row.Position, row.IsActive, row.Revision,
        uses.Where(use => use.AttributeId == row.Id).DistinctBy(use => use.DefinitionId).Select(use => new FormPersonAttributeUseResponse(use.DefinitionId, Text(use.NameJson))).ToArray());
    private static Dictionary<string, string> Names(IReadOnlyDictionary<string, string?> values) => values.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value!.Trim(), StringComparer.Ordinal);
    private static Dictionary<string, string> Texts(string json) => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("The stored person attribute names are invalid.");
    private static string Text(string json) { var values = Texts(json); return values.GetValueOrDefault(Culture()) ?? values.GetValueOrDefault("es") ?? ""; }
    private static string Culture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
    [LoggerMessage(Level = LogLevel.Information, Message = "Person data configuration {SubjectId} saved.")]
    private static partial void LogSaved(ILogger logger, Guid subjectId);
}
