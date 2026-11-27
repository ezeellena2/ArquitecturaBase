using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public static class AiModelPriceErrors
{
    public static readonly Error Invalid = Error.Validation("Ai.ModelPrice.Invalid",
        "The model needs a name, and each price goes from zero to its maximum with up to six decimals.");
    public static readonly Error RevisionConflict = Error.Conflict("Ai.ModelPrice.RevisionConflict", "The prices changed. Review their current revision.");
}
