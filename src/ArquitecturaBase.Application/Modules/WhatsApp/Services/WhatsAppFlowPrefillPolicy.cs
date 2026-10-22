using System.Collections.ObjectModel;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal static class WhatsAppFlowPrefillPolicy
{
    public const string ProfileRevision = WhatsAppFlowProfile.Revision;
    public static string DataKey(string screenId) => "prefill_" + screenId;
    public static Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Build(WhatsAppFlowProjection projection,
        IReadOnlyDictionary<string, string> values)
    {
        if (projection.CodecVersion != 1 || projection.Format != 1) return FlowErrors.InvalidValue;
        if (!WhatsAppFlowProfile.PrefillRevisions.Contains(projection.ProfileRevision))
            return values.Count == 0 ? Result.Success<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(new Dictionary<string, IReadOnlyDictionary<string, string>>()) : FlowErrors.UnsupportedConfiguration;
        if (values.Keys.Any(key => !projection.Mapping.Any(field => field.Key == key && field.Files is null))) return FlowErrors.InvalidValue;
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var groups = WhatsAppFlowScreenBuilder.Groups(projection.Mapping.Select(field => field.Files is not null).ToArray());
        for (var screen = 0; screen < groups.Count; screen++)
        {
            var scalars = groups[screen].Select(index => projection.Mapping[index]).Where(field => field.Files is null).ToArray();
            if (scalars.Length == 0) continue;
            var initial = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in scalars)
            {
                if (!values.TryGetValue(field.Key, out var value)) continue;
                if (string.IsNullOrEmpty(value)) return FlowErrors.InvalidValue;
                // Dynamic choice membership was checked against the recipient offer by its owner.
                if (field.Type == "choice" && field.Choices.Count == 0)
                {
                    if (!Guid.TryParseExact(value, "D", out var choice) || choice == Guid.Empty) return FlowErrors.InvalidValue;
                }
                else
                {
                    var decoded = WhatsAppFlowValueCodec.Decode(new(field.FieldId, field.Key, field.Type, false, field.MaxLength, field.Choices, field.Files), value);
                    if (decoded.IsFailure || !decoded.Value.IsPresent) return FlowErrors.InvalidValue;
                }
                initial.Add(field.Key, value);
            }
            if (initial.Count > 0)
                result.Add(DataKey(WhatsAppFlowScreenBuilder.ScreenId(screen)), new ReadOnlyDictionary<string, string>(initial));
        }
        return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(result);
    }
}
