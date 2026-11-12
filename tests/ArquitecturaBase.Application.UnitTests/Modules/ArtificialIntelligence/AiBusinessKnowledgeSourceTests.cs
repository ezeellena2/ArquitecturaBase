using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

/// <summary>
/// Casos del asistente (AS5): the business information the assistant may use. Only confirmed facts: a question without an answer
/// and an earlier text not reviewed are never facts; each zone of «Información del negocio» says its state.
/// </summary>
public sealed class AiBusinessKnowledgeSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Answered = Guid.Parse("018f0000-0000-7000-8000-0000000000a1");
    private static readonly Guid Unanswered = Guid.Parse("018f0000-0000-7000-8000-0000000000a2");

    [Fact]
    public async Task Without_business_information_every_zone_is_empty()
    {
        var knowledge = await new AiBusinessKnowledgeSource(new Reader(AiBusinessInformationResponse.Empty)).ReadAsync("es", Ct);

        Assert.Equal(BusinessKnowledgeAvailability.Available, knowledge.Availability);
        Assert.Equal(0, knowledge.Revision);
        Assert.Equal([BusinessKnowledgeSectionKey.Details, BusinessKnowledgeSectionKey.Location, BusinessKnowledgeSectionKey.Hours,
            BusinessKnowledgeSectionKey.Services, BusinessKnowledgeSectionKey.Answers], knowledge.Sections.Select(section => section.Key));
        Assert.All(knowledge.Sections, section => Assert.Equal(BusinessKnowledgeSectionState.Empty, section.State));
        Assert.Empty(knowledge.Sections.SelectMany(section => section.Facts));
    }

    [Fact]
    public async Task Only_confirmed_facts_are_used_and_each_zone_says_its_state()
    {
        var information = new AiBusinessInformationResponse("Arquitectura Base", "Obras y mantenimiento.", "Lunes a viernes de 8 a 17",
            "Atención en el local", "Obras", "", "¿Hacen envíos? Sí", 4,
            new AiBusinessDetails(new("unknown", "", "", "", null, null),
                new("America/Argentina/Buenos_Aires", [new(0, "open", [new("08:00", "17:00", false)]), new(6, "closed", [])], []),
                [new(Answered, "¿Hacen envíos?", "Sí, al interior por transporte."), new(Unanswered, "¿Hay estacionamiento cerca?", "")]));

        var knowledge = await new AiBusinessKnowledgeSource(new Reader(information)).ReadAsync("es", Ct);

        Assert.Equal(4, knowledge.Revision);
        Assert.Equal(1, knowledge.UnansweredQuestions);
        Assert.Equal(BusinessKnowledgeSectionState.Loaded, Section(knowledge, BusinessKnowledgeSectionKey.Details).State);
        // The earlier location text is there, but nobody reviewed it: it is not a fact.
        Assert.Equal(BusinessKnowledgeSectionState.LegacyUnreviewed, Section(knowledge, BusinessKnowledgeSectionKey.Location).State);
        Assert.Empty(Section(knowledge, BusinessKnowledgeSectionKey.Location).Facts);
        Assert.Equal(BusinessKnowledgeSectionState.Loaded, Section(knowledge, BusinessKnowledgeSectionKey.Hours).State);
        Assert.Equal(BusinessKnowledgeSectionState.Loaded, Section(knowledge, BusinessKnowledgeSectionKey.Services).State);
        var answer = Assert.Single(Section(knowledge, BusinessKnowledgeSectionKey.Answers).Facts);
        Assert.Equal("faq." + Answered.ToString("N"), answer.Id);
        Assert.Equal("¿Hacen envíos?", answer.Label);
        Assert.Equal("Sí, al interior por transporte.", answer.Text);
        var texts = knowledge.Sections.SelectMany(section => section.Facts).Select(fact => fact.Text).ToArray();
        Assert.DoesNotContain("Lunes a viernes de 8 a 17", texts);
        Assert.DoesNotContain("Atención en el local", texts);
        Assert.DoesNotContain(texts, text => text.Contains("estacionamiento", StringComparison.Ordinal));
        Assert.Contains(Section(knowledge, BusinessKnowledgeSectionKey.Hours).Facts, fact => fact.Label == "Lunes" && fact.Text == "08:00–17:00");
        Assert.Contains(Section(knowledge, BusinessKnowledgeSectionKey.Hours).Facts, fact => fact.Label == "Domingo" && fact.Text == "Cerrado");
    }

    [Fact]
    public async Task Earlier_hours_and_answers_without_the_guided_ones_are_not_reviewed()
    {
        var information = new AiBusinessInformationResponse("Arquitectura Base", "", "De 8 a 17", "", "", "", "Preguntas viejas", 2);

        var knowledge = await new AiBusinessKnowledgeSource(new Reader(information)).ReadAsync("en", Ct);

        Assert.Equal(BusinessKnowledgeSectionState.LegacyUnreviewed, Section(knowledge, BusinessKnowledgeSectionKey.Hours).State);
        Assert.Equal(BusinessKnowledgeSectionState.LegacyUnreviewed, Section(knowledge, BusinessKnowledgeSectionKey.Answers).State);
        Assert.Equal(BusinessKnowledgeSectionState.Empty, Section(knowledge, BusinessKnowledgeSectionKey.Location).State);
        Assert.Equal(BusinessKnowledgeSectionState.Empty, Section(knowledge, BusinessKnowledgeSectionKey.Services).State);
        var name = Assert.Single(Section(knowledge, BusinessKnowledgeSectionKey.Details).Facts);
        Assert.Equal("Name", name.Label);
    }

    private static BusinessKnowledgeSection Section(BusinessKnowledge knowledge, BusinessKnowledgeSectionKey key) =>
        Assert.Single(knowledge.Sections, section => section.Key == key);

    private sealed class Reader(AiBusinessInformationResponse information) : IAiBusinessInformationReader
    {
        public Task<AiBusinessInformationResponse> FindAsync(CancellationToken cancellationToken) => Task.FromResult(information);
        public Task<AiBusinessInformationChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult<AiBusinessInformationChangeResponse?>(null);
    }
}
