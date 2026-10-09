using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record AiBusinessInformationResponse(string Name, string Description, string Hours, string Location,
    string Services, string Policies, string Faq, long Revision, AiBusinessDetails? Details = null)
{
    public static AiBusinessInformationResponse Empty { get; } = new("", "", "", "", "", "", "", 0);
    public override string ToString() => "AiBusinessInformationResponse { content: [REDACTED] }";
}
public sealed record AiBusinessInformationChangeResponse(Guid RequestId, long Revision);
