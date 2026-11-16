namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
/// <summary>I17: the control value of each key of the configured ring, by key identifier. It never carries the key.</summary>
public interface IAiKeyRingControl
{
    IReadOnlyDictionary<string, string> CreateControls();
}
