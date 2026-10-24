using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormCatalogFieldSaveRequest(string? LocalId, Guid? Id, string? Key,
    IReadOnlyDictionary<string, string?>? Name, string? Type, Guid? SourceCatalogId, bool Required,
    bool Unique, string? Min, string? Max, int? MaxLength, string? Role, int Position, bool IsActive);

public sealed record FormCatalogFieldResponse(Guid Id, string Key, IReadOnlyDictionary<string, string> Name,
    string Type, Guid? SourceCatalogId, bool Required, bool Unique, string? Min, string? Max, int? MaxLength,
    string Role, int Position, bool IsActive, int ValuesCount, IReadOnlyList<FormCatalogFieldUsageResponse> UsedBy);

public sealed record FormCatalogFieldUsageResponse(Guid? DefinitionId, string? FormName, string? Question, Guid? QuestionId, string Stage);
public sealed record FormCatalogFieldUsageRow(Guid DefinitionId, string FormName, string Question, Guid QuestionId, string Stage, Guid FieldId, bool ByMatch);
public sealed record FormCatalogPresentationExample(Guid ItemId, string Title);
// A question's own presentation, checked over every active option of its source catalog without saving anything.
public sealed record FormCatalogPresentationCheckRequest(FormCatalogPresentation? Presentation);
public sealed record FormCatalogPresentationCheckResponse(IReadOnlyList<FormCatalogPresentationCultureCheckResponse> Cultures);
public sealed record FormCatalogPresentationCultureCheckResponse(string Culture, int TitleTooLongCount,
    IReadOnlyList<FormCatalogPresentationExample> TitleExamples, int DescriptionTooLongCount, IReadOnlyList<FormCatalogPresentationExample> DescriptionExamples);
public sealed record FormCatalogItemFieldValuesRow(Guid ItemId, Guid CatalogId, string? FieldsJson, string FieldValuesJson,
    string? TextEs = null, string? TextEn = null);
public sealed record FormCatalogCapturedField(Guid Id, string Key, IReadOnlyDictionary<string, string> Name,
    string Type, string Role, JsonElement Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Text = null);

// Previous holds the definitions the saved values were written under; Culture is the request culture (es/en) that
// reads values saved as text when their field changes type.
internal sealed record FormCatalogFieldContext(IReadOnlyList<ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormCatalogField> Fields,
    IReadOnlyDictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> Sources,
    IReadOnlySet<Guid> TypeChanges, IReadOnlySet<Guid> RequiredTransitions,
    IReadOnlyDictionary<Guid, ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormCatalogField> Previous, string Culture);
public sealed record FormCatalogInvalidFieldRow(Guid ItemId, string Code, int Row, string? Value, string Reason)
{ public override string ToString() => "FormCatalogInvalidFieldRow { Content = [REDACTED] }"; }

internal sealed record FormCatalogFieldPreparation(FormCatalogFieldContext Context, IReadOnlyDictionary<string, Guid> IdentityMap);

internal sealed record FormCatalogItemsRecorded(IReadOnlyDictionary<string, Guid> IdentityMap,
    IReadOnlyList<ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormCatalogItem> Items);
