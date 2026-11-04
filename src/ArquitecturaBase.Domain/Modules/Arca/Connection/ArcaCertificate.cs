using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>
/// Un certificado de la conexión. Nace pendiente con su pedido y su clave privada cifrada; el Id se conoce antes de cifrar,
/// porque la clave queda atada a él. En el alta, cargar y promover lo vuelve el activo; en una renovación se promueve
/// recién cuando el nuevo pasa la prueba.
/// </summary>
public sealed class ArcaCertificate : Entity
{
    private ArcaCertificate(Guid id) : base(id) { }

    public Guid ConnectionId { get; private set; }
    public ArcaCertificateRole Role { get; private set; }
    public string Alias { get; private set; } = string.Empty;
    public string RequestPem { get; private set; } = string.Empty;
    public string ProtectedPrivateKey { get; private set; } = string.Empty;
    public string? CertificatePem { get; private set; }
    public DateTime? NotBeforeUtc { get; private set; }
    public DateTime? NotAfterUtc { get; private set; }
    public string? Thumbprint { get; private set; }
    public bool IsLoaded => CertificatePem is not null;

    /// <summary>Entre 3 y 30 minúsculas, dígitos o guiones, empezando por letra.</summary>
    public static bool IsValidAlias(string? alias) =>
        alias is { Length: >= 3 and <= 30 } && char.IsAsciiLetterLower(alias[0])
        && alias.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    public static Result<ArcaCertificate> Request(Guid id, Guid connectionId, string alias, string requestPem, string protectedPrivateKey)
    {
        if (!IsValidAlias(alias)) return ArcaConnectionErrors.AliasInvalid;
        return new ArcaCertificate(id)
        {
            ConnectionId = connectionId,
            Role = ArcaCertificateRole.Pending,
            Alias = alias,
            RequestPem = requestPem,
            ProtectedPrivateKey = protectedPrivateKey,
        };
    }

    public void Load(string certificatePem, DateTime notBeforeUtc, DateTime notAfterUtc, string thumbprint)
    {
        CertificatePem = certificatePem;
        NotBeforeUtc = notBeforeUtc;
        NotAfterUtc = notAfterUtc;
        Thumbprint = thumbprint;
    }

    public void Promote()
    {
        if (!IsLoaded) throw new InvalidOperationException("Only a loaded ARCA certificate can become the active one.");
        Role = ArcaCertificateRole.Active;
    }

    public bool IsExpired(DateTime nowUtc) => NotAfterUtc is { } notAfter && notAfter <= nowUtc;

    public bool ExpiresWithin(TimeSpan window, DateTime nowUtc) => NotAfterUtc is { } notAfter && notAfter - nowUtc <= window;

    public override string ToString() => $"ArcaCertificate {{ id: {Id}, alias: {Alias}, role: {Role}, privateKey: [REDACTED] }}";
}
