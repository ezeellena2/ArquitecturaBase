using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormPublicationSnapshotPolicy
{
    public static StructuredFormCheckRequest ChannelRequest(FormDefinitionResponse saved, int? maxFileSizeMiB = null) => new(saved.Draft.Format,
        saved.Draft.Culture, saved.Name, saved.Draft.SubmitLabel,
        saved.Draft.Fields.Select(field => new StructuredFormQuestion(field.FieldId, field.Key, field.Type, field.Label, field.Help,
            field.Required, field.MaxLength, field.Options?.Select(option => new StructuredFormChoice(option.ChoiceId, option.Label)).ToArray(),
            field.Source?.Catalog, field.Files is null ? null : new StructuredFormFileSettings(field.Files.MinFiles, field.Files.MaxFiles, field.Files.PhotoSource))).ToArray(),
        FormPublicationPolicy.CheckCultures(saved).Where(culture => culture.IsComplete).Select(culture => culture.Culture).ToArray(),
        saved.Draft.Fields.Any(field => field.Files is not null) ? maxFileSizeMiB ?? 25 : null);

    public static string Hash(string canonical)
    {
        var bytes = Encoding.UTF8.GetBytes(canonical);
        try { return Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static string Fingerprint(FormDefinitionResponse saved, IReadOnlyList<FormPublicationProfile> profiles) =>
        Hash(FormDraftPolicy.Serialize(new { saved.Id, saved.Revision, saved.Name, saved.Prefix, saved.Draft, profiles }));

    public static bool Matches(SaveFormDraftRequest input, FormDefinitionResponse saved)
    {
        if (FormDraftPolicy.Serialize(input.Name) != FormDraftPolicy.Serialize(saved.Name) || input.Prefix != saved.Prefix || input.Draft.Culture != saved.Draft.Culture
            || input.Draft.Fields.Any(field => !field.FieldId.HasValue || field.ReplacesFieldId.HasValue
                || (field.Options?.Any(option => !option.ChoiceId.HasValue) ?? false))) return false;
        var fields = input.Draft.Fields.Select(field => new CanonicalFormField(field.FieldId!.Value, "field_" + field.FieldId.Value.ToString("N"),
            field.Type, field.Label, CleanHelp(field.Help), field.Required, field.MaxLength,
            field.Options?.Select(option => new CanonicalFormChoice(option.ChoiceId!.Value, option.Label)).ToArray(), field.Source, field.Files)).ToArray();
        return FormDraftPolicy.Serialize(new FormDraftResponse(input.Draft.Format, input.Draft.Culture, input.Draft.SubmitLabel!, fields))
            == FormDraftPolicy.Serialize(saved.Draft);
    }
    private static Dictionary<string, string>? CleanHelp(IReadOnlyDictionary<string, string?>? input)
    {
        var values = input?.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
        return values is { Count: > 0 } ? values : null;
    }
}
