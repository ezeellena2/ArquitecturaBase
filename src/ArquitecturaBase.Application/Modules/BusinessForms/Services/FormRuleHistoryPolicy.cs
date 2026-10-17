using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormRuleHistoryPolicy
{
    public static FormRuleChangesResponse Compare(IReadOnlyList<FormRule> current, IReadOnlyList<FormRule> previous)
    {
        var previousById = previous.ToDictionary(rule => rule.Id);
        var currentIds = current.Select(rule => rule.Id).ToHashSet();
        return new(
            current.Count(rule => !previousById.ContainsKey(rule.Id)),
            current.Count(rule => previousById.TryGetValue(rule.Id, out var before) && !SameRule(rule, before)),
            previous.Count(rule => !currentIds.Contains(rule.Id)),
            !current.Where(rule => previousById.ContainsKey(rule.Id)).Select(rule => rule.Id)
                .SequenceEqual(previous.Where(rule => currentIds.Contains(rule.Id)).Select(rule => rule.Id)));
    }
    private static bool SameRule(FormRule current, FormRule previous) =>
        current.Event == previous.Event
        && current.Conditions.Count == previous.Conditions.Count
        && current.Conditions.Zip(previous.Conditions).All(pair => pair.First.FieldId == pair.Second.FieldId
            && pair.First.Operator == pair.Second.Operator && SameValues(pair.First.Values, pair.Second.Values))
        && current.Actions.Count == previous.Actions.Count
        && current.Actions.Zip(previous.Actions).All(pair => pair.First.Id == pair.Second.Id
            && pair.First.Kind == pair.Second.Kind && pair.First.RoleId == pair.Second.RoleId
            && pair.First.IncludePdf == pair.Second.IncludePdf && SameText(pair.First.Text, pair.Second.Text));
    private static bool SameValues(IReadOnlyList<string>? current, IReadOnlyList<string>? previous) =>
        current is null ? previous is null : previous is not null && current.SequenceEqual(previous);
    private static bool SameText(IReadOnlyDictionary<string,string>? current, IReadOnlyDictionary<string,string>? previous) =>
        current is null ? previous is null : previous is not null && current.Count == previous.Count
            && current.All(pair => previous.TryGetValue(pair.Key, out var value) && pair.Value == value);
}
