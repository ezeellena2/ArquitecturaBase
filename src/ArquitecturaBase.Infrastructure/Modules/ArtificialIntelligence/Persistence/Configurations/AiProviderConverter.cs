using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;

/// <summary>
/// I4: the provider is kept by its name, as before. A name this version does not know (a newer one saved it) reads as
/// <see cref="AiProviders.Unsupported"/> instead of failing every read with a 500; that value is never written over the saved name.
/// </summary>
internal sealed class AiProviderConverter() : ValueConverter<AiProvider, string>(provider => Write(provider), name => Read(name))
{
    private static string Write(AiProvider provider) => AiProviders.IsSupported(provider) ? provider.ToString()
        : throw new InvalidOperationException("An AI provider this version does not know is never written.");

    private static AiProvider Read(string name) =>
        Enum.TryParse<AiProvider>(name, false, out var provider) && AiProviders.IsSupported(provider) && provider.ToString() == name
            ? provider : AiProviders.Unsupported;
}
