using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormCatalogFieldSaveRequest(string? LocalId, Guid? Id, string? Key,
    IReadOnlyDictionary<string, string?>? Name, string? Type, Guid? SourceCatalogId, bool Required,
    bool Unique, string? Min, string? Max, int? MaxLength, string? Role, int Position, bool IsActive);

public sealed record FormCatalogFieldResponse(Guid Id, string Key, IReadOnlyDictionary<string, string> Name,
    string Type, Guid? SourceCatalogId, bool Required, bool Unique, string? Min, string? Max, int? MaxLength,
    string Role, int Position, bool IsActive, bool HasValues, IReadOnlyList<FormCatalogFieldUsageResponse> UsedBy);

public sealed record FormCatalogFieldUsageResponse(Guid DefinitionId, string FormName, string Question, string Stage);
public sealed record FormCatalogFieldUsageRow(Guid DefinitionId, string FormName, string Question, string Stage);
public sealed record FormCatalogPresentationExample(Guid ItemId, string Title);
public sealed record FormCatalogPresentationCheck(int TitleTooLongCount, IReadOnlyList<FormCatalogPresentationExample> Examples);
public sealed record FormCatalogItemFieldValuesRow(Guid ItemId, Guid CatalogId, string? FieldsJson, string FieldValuesJson,
    string? TextEs = null, string? TextEn = null);
public sealed record FormCatalogCapturedField(Guid Id, string Key, IReadOnlyDictionary<string, string> Name,
    string Type, string Role, JsonElement Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Text = null);

internal sealed record FormCatalogFieldContext(IReadOnlyList<ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormCatalogField> Fields,
    IReadOnlyDictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> Sources,
    IReadOnlySet<Guid> TypeChanges, IReadOnlySet<Guid> RequiredTransitions);
public sealed record FormCatalogInvalidFieldRow(Guid ItemId, string Code);

internal sealed record FormCatalogFieldPreparation(FormCatalogFieldContext Context, IReadOnlyDictionary<string, Guid> IdentityMap);

internal sealed record FormCatalogItemsRecorded(IReadOnlyDictionary<string, Guid> IdentityMap,
    IReadOnlyList<ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormCatalogItem> Items);
