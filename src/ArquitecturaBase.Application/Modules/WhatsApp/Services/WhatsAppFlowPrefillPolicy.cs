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
        // The block and its rows' questions are never prefilled, nor a question that chooses first (LD2-A): its list is empty
        // until a value is chosen. Nor a calculation (CA1-A), which the server computes, or a question with a condition (CO1-A).
        if (values.Keys.Any(key => !projection.Mapping.Any(field => field.Key == key && field.Files is null && field.Type is not ("multiChoice" or "group")
            && field.Repeated != true && field.ChoosesFirst != true && field.Calculated != true && field.ShowWhen is null))) return FlowErrors.InvalidValue;
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        // A question whose options set the boolean of another (CO1-A, several answers) opens empty, so that boolean opens false:
        // its proposed value is left out and the rest still goes.
        var setsShown = projection.Mapping.Where(field => field.ShowWhen is { Values.Count: > 1 }).Select(field => field.ShowWhen!.FieldId).ToHashSet();
        var screens = WhatsAppFlowScreenBuilder.Plan(projection.Mapping.Select(field =>
            new WhatsAppFlowSlot(field.Type, field.Files is not null, field.Repeated == true, field.Rows, field.Calculated == true)).ToArray());
        foreach (var screen in screens.Where(screen => screen.Kind == "fields"))
        {
            var scalars = screen.Questions.Select(index => projection.Mapping[index]).Where(field => field.Files is null && field.Type != "multiChoice").ToArray();
            if (scalars.Length == 0) continue;
            var initial = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in scalars)
            {
                if (!values.TryGetValue(field.Key, out var value) || setsShown.Contains(field.FieldId)) continue;
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
                result.Add(DataKey(screen.Id), new ReadOnlyDictionary<string, string>(initial));
        }
        return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(result);
    }
}
