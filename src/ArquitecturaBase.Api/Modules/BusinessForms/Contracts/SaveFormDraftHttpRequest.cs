using System.Text.Json.Serialization;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveFormDraftHttpRequest(Guid RequestId, IReadOnlyDictionary<string, string> Name, string Prefix, FormDraftHttpRequest Draft, long? ExpectedRevision = null, string? OfferWhen = null)
{ public override string ToString() => "SaveFormDraftHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormDraftHttpRequest(int Format, string Culture, IReadOnlyDictionary<string, string>? SubmitLabel, IReadOnlyList<FormFieldHttpRequest> Fields,
    IReadOnlyList<FormSectionHttpRequest>? Sections = null)
{ public override string ToString() => "FormDraftHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormSectionHttpRequest(int Start, IReadOnlyDictionary<string, string> Title)
{ public override string ToString() => "FormSectionHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormFieldHttpRequest(string LocalId, Guid? FieldId, Guid? ReplacesFieldId, string Type,
    IReadOnlyDictionary<string, string> Label, IReadOnlyDictionary<string, string?>? Help, bool Required, int? MaxLength,
    IReadOnlyList<FormChoiceHttpRequest>? Options, FormQuestionSourceHttpRequest? Source = null, FormFileSettingsHttpRequest? Files = null, FormSelectionSettingsHttpRequest? Selection = null,
    FormRowSettingsHttpRequest? Rows = null, bool? Repeated = null, FormCalculationHttpRequest? Calculation = null, FormShowWhenHttpRequest? ShowWhen = null)
{ public override string ToString() => "FormFieldHttpRequest { Content = [REDACTED] }"; }
// CA1-A: a read-only decimal computed with "multiply", "sum" or "percent" over questions named by FieldId or, if new, by LocalId.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCalculationHttpRequest(string Operation, IReadOnlyList<Guid> Operands)
{ public override string ToString() => "FormCalculationHttpRequest { Content = [REDACTED] }"; }
// CO1-A: the earlier question (FieldId or, if new, LocalId) and the answers that show this one.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormShowWhenHttpRequest(Guid QuestionId, IReadOnlyList<string> Values)
{ public override string ToString() => "FormShowWhenHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormQuestionSourceHttpRequest(Guid? Catalog, IReadOnlyList<FormCatalogMatchHttpRequest>? Match = null,
    FormCatalogPresentationHttpRequest? Presentation = null, Guid? GroupBy = null)
{ public override string ToString() => "FormQuestionSourceHttpRequest { Content = [REDACTED] }"; }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCatalogMatchHttpRequest(Guid FieldId, string? Value = null, Guid? PersonAttributeId = null, Guid? QuestionId = null)
{ public override string ToString() => "FormCatalogMatchHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormChoiceHttpRequest(string LocalId, Guid? ChoiceId, IReadOnlyDictionary<string, string> Label)
{ public override string ToString() => "FormChoiceHttpRequest { Content = [REDACTED] }"; }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormSelectionSettingsHttpRequest(int MinSelected, int MaxSelected);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormRowSettingsHttpRequest(int MinRows, int MaxRows);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormFileSettingsHttpRequest(int MinFiles, int MaxFiles, string? PhotoSource = null);
