using System.Text.Json.Serialization;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveFormDraftHttpRequest(Guid RequestId, IReadOnlyDictionary<string, string> Name, string Prefix, FormDraftHttpRequest Draft, long? ExpectedRevision = null, string? OfferWhen = null)
{ public override string ToString() => "SaveFormDraftHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormDraftHttpRequest(int Format, string Culture, IReadOnlyDictionary<string, string>? SubmitLabel, IReadOnlyList<FormFieldHttpRequest> Fields)
{ public override string ToString() => "FormDraftHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormFieldHttpRequest(string LocalId, Guid? FieldId, Guid? ReplacesFieldId, string Type,
    IReadOnlyDictionary<string, string> Label, IReadOnlyDictionary<string, string?>? Help, bool Required, int? MaxLength,
    IReadOnlyList<FormChoiceHttpRequest>? Options, FormQuestionSourceHttpRequest? Source = null, FormFileSettingsHttpRequest? Files = null, FormSelectionSettingsHttpRequest? Selection = null)
{ public override string ToString() => "FormFieldHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormQuestionSourceHttpRequest(Guid? Catalog);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormChoiceHttpRequest(string LocalId, Guid? ChoiceId, IReadOnlyDictionary<string, string> Label)
{ public override string ToString() => "FormChoiceHttpRequest { Content = [REDACTED] }"; }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormSelectionSettingsHttpRequest(int MinSelected, int MaxSelected);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormFileSettingsHttpRequest(int MinFiles, int MaxFiles, string? PhotoSource = null);
