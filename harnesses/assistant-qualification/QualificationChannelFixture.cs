namespace AssistantQualification;

internal sealed record QualificationForm(Guid DefinitionId, Guid VersionId, string Alias, IReadOnlyDictionary<string,Guid> Fields)
{
    public IReadOnlyDictionary<string,Guid> ChoiceIds { get; init; } = new Dictionary<string,Guid>();
    // Public fixture names (es/en) and prefix: an authoring proposal must not repeat them.
    public IReadOnlyDictionary<string,string> Names { get; init; } = new Dictionary<string,string>();
    public string Prefix { get; init; } = "";
}
internal sealed record QualificationCatalog(string Alias, Guid Id, IReadOnlyDictionary<string,string> Names);
internal sealed record QualificationSuggestion(Guid SubjectUserId, Guid SourceRecordId, string SourceNumber, Guid SuggestionId, Guid TargetDefinitionId);
internal sealed record QualificationOffer(Guid SubjectUserId, Guid RequestId, Guid DefinitionId, Guid VersionId);
internal sealed record QualificationChannelFixture(IReadOnlyList<QualificationForm> Forms,
    IReadOnlyList<QualificationSuggestion> Suggestions, IReadOnlyList<QualificationOffer> PriorOffers)
{
    public IReadOnlyList<QualificationCatalog> Catalogs { get; init; } = [];
    public QualificationForm Form(string alias) => Forms.Single(form => form.Alias == alias);
    public QualificationCatalog Catalog(string alias) => Catalogs.Single(catalog => catalog.Alias == alias);
}
