namespace AssistantQualification;

internal sealed record QualificationForm(Guid DefinitionId, Guid VersionId, string Alias, IReadOnlyDictionary<string,Guid> Fields)
{
    public IReadOnlyDictionary<string,Guid> ChoiceIds { get; init; } = new Dictionary<string,Guid>();
}
internal sealed record QualificationSuggestion(Guid SubjectUserId, Guid SourceRecordId, string SourceNumber, Guid SuggestionId, Guid TargetDefinitionId);
internal sealed record QualificationOffer(Guid SubjectUserId, Guid RequestId, Guid DefinitionId, Guid VersionId);
internal sealed record QualificationChannelFixture(IReadOnlyList<QualificationForm> Forms,
    IReadOnlyList<QualificationSuggestion> Suggestions, IReadOnlyList<QualificationOffer> PriorOffers)
{
    public QualificationForm Form(string alias) => Forms.Single(form => form.Alias == alias);
}
