using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantEvidenceVerifierTests
{
    [Fact]
    public void Foreign_or_unknown_evidence_prevents_publication()
    {
        var id = Guid.NewGuid();
        var evidence = new SystemQueryEvidence(id, "users.summary", 1, new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), null, "users", [], []);
        Assert.Equal(AiAssistantErrors.InvalidOutput,
            AiAssistantEvidenceVerifier.Verify(new("answer", "There are active users.", [Guid.NewGuid()]), [evidence]).Error);
        Assert.True(AiAssistantEvidenceVerifier.Verify(new("answer", "See the confirmed totals.", [id]), [evidence]).IsSuccess);
    }

    [Fact]
    public void A_factual_answer_requires_evidence_from_this_operation()
    {
        Assert.Equal(AiAssistantErrors.InvalidOutput, AiAssistantEvidenceVerifier.Verify(new("answer", "There are no users.", []), []).Error);
        Assert.True(AiAssistantEvidenceVerifier.Verify(new("clarification", "Which role do you mean?", []), []).IsSuccess);
        Assert.Equal(AiAssistantErrors.InvalidOutput, AiAssistantEvidenceVerifier.Verify(new("unsupported", "There are 20 users.", []), []).Error);
    }
}
