using ArquitecturaBase.Application.Interfaces.Channels;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// Los PDF fiscales (<c>arca-vouchers/{id}.pdf</c>) no se limpian nunca: son documentos con valor legal. No se mira la base,
/// porque después de restaurarla puede no tener el comprobante de un PDF que ARCA autorizó (H3). Sólo lee.
/// </summary>
internal sealed class ArcaPrivateFileRetentionSource : IPrivateFileRetentionSource
{
    private const string Prefix = "arca-vouchers/";
    private const string Extension = ".pdf";

    public Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([.. storageKeys.Where(key => key.StartsWith(Prefix, StringComparison.Ordinal)
            && key.EndsWith(Extension, StringComparison.Ordinal) && Guid.TryParseExact(key[Prefix.Length..^Extension.Length], "N", out _)).Distinct(StringComparer.Ordinal)]);
}
