namespace ArquitecturaBase.Application.Models.Assistant;

public enum BusinessKnowledgeAvailability { NotInstalled, Available }

/// <summary>The zones of «Información del negocio», in its order: data, location, hours, services and policies, and answers.</summary>
public enum BusinessKnowledgeSectionKey { Details, Location, Hours, Services, Answers }

/// <summary>
/// Loaded: it has confirmed facts. Empty: nothing loaded. LegacyUnreviewed: only a text saved before the guided editor, which
/// nobody reviewed, so it is never used as a fact.
/// </summary>
public enum BusinessKnowledgeSectionState { Loaded, Empty, LegacyUnreviewed }

/// <summary>A confirmed fact with a stable id (what «Usó» names), its label and its text, in the culture asked for.</summary>
public sealed record BusinessKnowledgeFact(string Id, string Label, string Text)
{
    public override string ToString() => "BusinessKnowledgeFact { content: [REDACTED] }";
}

public sealed record BusinessKnowledgeSection(BusinessKnowledgeSectionKey Key, BusinessKnowledgeSectionState State, IReadOnlyList<BusinessKnowledgeFact> Facts);

/// <summary>What the assistant may use of the business information: only confirmed facts, zone by zone, and how many questions lack an answer.</summary>
public sealed record BusinessKnowledge(BusinessKnowledgeAvailability Availability, long Revision, IReadOnlyList<BusinessKnowledgeSection> Sections,
    int UnansweredQuestions)
{
    public static BusinessKnowledge NotInstalled { get; } = new(BusinessKnowledgeAvailability.NotInstalled, 0, [], 0);

    public IEnumerable<BusinessKnowledgeFact> Facts => Sections.SelectMany(section => section.Facts);

    public override string ToString() => "BusinessKnowledge { content: [REDACTED] }";
}
