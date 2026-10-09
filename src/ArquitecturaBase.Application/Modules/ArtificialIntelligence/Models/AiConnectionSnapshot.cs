using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record AiConnectionSnapshot(AiProvider Provider, string Model, string? ProtectedApiKey, bool Enabled, long Revision)
{
    public AiConnectionResponse ToResponse() => new(Provider, Model, ProtectedApiKey is not null, Enabled, Revision);
    public override string ToString() => "AiConnectionSnapshot { credential: [REDACTED] }";
}
