using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Channels;

/// <summary>
/// ME2c: the file cleanup keeps the logo and the icon the settings point to; a replaced or removed one stops being referenced
/// and goes with the next sweep. Reads the row, never writes.
/// </summary>
internal sealed class BrandImageRetentionSource(ISystemSettingsRepository repository) : IPrivateFileRetentionSource
{
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        if (!storageKeys.Any(BrandImage.IsAnyKey)) return [];
        var settings = await repository.GetAsync(cancellationToken);
        string?[] current = [settings?.LogoFileKey, settings?.IconFileKey];
        return [.. storageKeys.Where(key => current.Contains(key, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)];
    }
}
