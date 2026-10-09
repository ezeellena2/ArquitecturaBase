using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record UpdateAiBusinessInformationRequest(string Name, string Description, string Hours, string Location,
    string Services, string Policies, string Faq, long ExpectedRevision, Guid RequestId, AiBusinessDetails? Details = null)
{
    public override string ToString() => "UpdateAiBusinessInformationRequest { content: [REDACTED] }";
}
