namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>Capabilities installed by optional modules, without account or provider state.</summary>
public interface IClientCapabilitySource
{
    IReadOnlyCollection<string> InstalledFeatures { get; }
}
