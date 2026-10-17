using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormSuggestionHistoryPolicy
{
    public static FormSuggestionChangesResponse Compare(IReadOnlyList<FormSuggestion> current,IReadOnlyList<FormSuggestion> previous)
    {
        var before=previous.ToDictionary(suggestion=>suggestion.Id);
        var ids=current.Select(suggestion=>suggestion.Id).ToHashSet();
        return new(current.Count(suggestion=>!before.ContainsKey(suggestion.Id)),
            current.Count(suggestion=>before.TryGetValue(suggestion.Id,out var old)&&!Same(suggestion,old)),
            previous.Count(suggestion=>!ids.Contains(suggestion.Id)),
            !current.Where(suggestion=>before.ContainsKey(suggestion.Id)).Select(suggestion=>suggestion.Id)
                .SequenceEqual(previous.Where(suggestion=>ids.Contains(suggestion.Id)).Select(suggestion=>suggestion.Id)));
    }
    private static bool Same(FormSuggestion current,FormSuggestion previous)=>current.DestinationDefinitionId==previous.DestinationDefinitionId
        &&current.Mode==previous.Mode&&current.When==previous.When&&current.IsActive==previous.IsActive
        &&current.Mappings.SequenceEqual(previous.Mappings)
        &&current.Conditions.Count==previous.Conditions.Count&&current.Conditions.Zip(previous.Conditions).All(pair=>pair.First.FieldId==pair.Second.FieldId&&pair.First.Operator==pair.Second.Operator
            &&(pair.First.Values is null?pair.Second.Values is null:pair.Second.Values is not null&&pair.First.Values.SequenceEqual(pair.Second.Values)));
}
