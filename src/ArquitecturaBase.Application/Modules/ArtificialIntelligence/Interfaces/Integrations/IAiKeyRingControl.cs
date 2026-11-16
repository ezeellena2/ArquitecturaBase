namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
/// <summary>I17: the control value of each key of the configured ring, by key identifier. It never carries the key.</summary>
public interface IAiKeyRingControl
{
    /// <summary>The key new ciphertexts and fingerprints use; once there are controls, it must already have its own (I17, review).</summary>
    string ActiveKeyId { get; }
    IReadOnlyDictionary<string, string> CreateControls();
}
