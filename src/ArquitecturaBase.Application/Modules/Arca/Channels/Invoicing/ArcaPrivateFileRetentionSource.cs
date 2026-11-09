using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// Los PDF fiscales (<c>arca-vouchers/{id}.pdf</c>) no se limpian mientras exista su comprobante, aunque todavía no tengan la
/// referencia guardada: son documentos con valor legal. Sólo lee.
/// </summary>
internal sealed class ArcaPrivateFileRetentionSource(IArcaVoucherReader reader) : IPrivateFileRetentionSource
{
    private const string Prefix = "arca-vouchers/";
    private const string Extension = ".pdf";

    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var key in storageKeys)
        {
            if (key.StartsWith(Prefix, StringComparison.Ordinal) && key.EndsWith(Extension, StringComparison.Ordinal)
                && Guid.TryParseExact(key[Prefix.Length..^Extension.Length], "N", out var voucherId))
                candidates.TryAdd(key, voucherId);
        }

        if (candidates.Count == 0) return [];
        var existing = (await reader.ListExistingIdsAsync([.. candidates.Values.Distinct()], cancellationToken)).ToHashSet();
        return [.. candidates.Where(candidate => existing.Contains(candidate.Value)).Select(candidate => candidate.Key)];
    }
}
