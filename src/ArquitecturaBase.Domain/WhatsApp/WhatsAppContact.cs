using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.WhatsApp;

/// <summary>
/// Una persona que le escribió al bot, tenga cuenta o no (sección 6.5 del spec del ingreso con WhatsApp). Se la
/// reconoce por el BSUID (<see cref="UserIdentifier"/>) y, mientras WhatsApp lo siga mandando, por el número
/// (<see cref="WaId"/>): se busca primero por el BSUID y después por el número. Si la persona cambia de número,
/// WhatsApp le da otro BSUID y aparece como un contacto nuevo; por eso un contacto nunca cambia de BSUID.
/// </summary>
public sealed class WhatsAppContact : AggregateRoot
{
    /// <summary>Un <c>wa_id</c> tiene hasta 15 dígitos; el resto es margen.</summary>
    public const int MaxWaIdLength = 32;

    /// <summary>
    /// Meta documenta los BSUID con "hasta 256 caracteres alfanuméricos". Hoy llegan más cortos, como
    /// "AR.1102953142229032" o "user." y 64 caracteres hexadecimales, pero uno más largo es igual de válido: si no
    /// entrara, el mensaje de alguien sin número se perdería.
    /// </summary>
    public const int MaxUserIdentifierLength = 256;

    public const int MaxProfileNameLength = 256;

    // Para EF Core.
    private WhatsAppContact()
    {
    }

    private WhatsAppContact(string? waId, string? userIdentifier, string? profileName, DateTime inboundAtUtc)
    {
        WaId = waId;
        UserIdentifier = userIdentifier;
        ProfileName = profileName;
        LastInboundAtUtc = inboundAtUtc;
    }

    /// <summary>El número tal como lo manda WhatsApp en <c>wa_id</c>: solo dígitos, sin el "+". Puede no llegar.</summary>
    public string? WaId { get; private set; }

    /// <summary>
    /// El BSUID, el identificador de la persona para este negocio (<c>user_id</c>). Es único: el mismo BSUID es
    /// siempre el mismo contacto.
    /// </summary>
    public string? UserIdentifier { get; private set; }

    /// <summary>El nombre que la persona tiene en su perfil de WhatsApp, el último que llegó.</summary>
    public string? ProfileName { get; private set; }

    /// <summary>La cuenta de la persona, si ya se la vinculó (lo hace el bot). Una cuenta tiene un solo contacto.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Cuándo escribió por última vez, con la hora de Meta.</summary>
    public DateTime LastInboundAtUtc { get; private set; }

    /// <summary>
    /// El primer mensaje de alguien que no se conocía. Hace falta el BSUID o el número; los vacíos cuentan como
    /// ausentes, porque así los manda Meta cuando no los tiene.
    /// </summary>
    public static WhatsAppContact Create(string? waId, string? userIdentifier, string? profileName, DateTime inboundAtUtc)
    {
        var normalizedWaId = NormalizeWaId(waId);
        var normalizedUserIdentifier = NormalizeUserIdentifier(userIdentifier);

        if (normalizedWaId is null && normalizedUserIdentifier is null)
        {
            throw new ArgumentException("A WhatsApp contact needs its BSUID or its wa_id.", nameof(userIdentifier));
        }

        return new WhatsAppContact(normalizedWaId, normalizedUserIdentifier, NormalizeProfileName(profileName), inboundAtUtc);
    }

    public static bool IsValidWaId(string? waId) =>
        waId is { Length: > 0 and <= MaxWaIdLength } && !waId.AsSpan().ContainsAnyExceptInRange('0', '9');

    /// <summary>Caracteres visibles de ASCII, sin espacios: así llega hoy, y así no se cuela nada raro en un índice.</summary>
    public static bool IsValidUserIdentifier(string? userIdentifier) =>
        userIdentifier is { Length: > 0 and <= MaxUserIdentifierLength }
        && !userIdentifier.AsSpan().ContainsAnyExceptInRange('!', '~');

    /// <summary>
    /// Registra otro mensaje de la persona. Meta reintenta durante días y manda los eventos desordenados: el nombre y
    /// el número se reemplazan solo con un mensaje igual o más nuevo que el último, aunque lo que faltaba se completa
    /// con cualquiera. El BSUID se adopta si el contacto no tenía, y uno distinto es un error de quien llama: es otro
    /// contacto.
    /// </summary>
    public void RecordInbound(string? waId, string? userIdentifier, string? profileName, DateTime inboundAtUtc)
    {
        var normalizedWaId = NormalizeWaId(waId);
        var normalizedUserIdentifier = NormalizeUserIdentifier(userIdentifier);
        var normalizedProfileName = NormalizeProfileName(profileName);

        if (normalizedUserIdentifier is not null)
        {
            if (UserIdentifier is not null && !string.Equals(UserIdentifier, normalizedUserIdentifier, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A message with another BSUID belongs to another WhatsApp contact.");
            }

            UserIdentifier = normalizedUserIdentifier;
        }

        var isLatest = inboundAtUtc >= LastInboundAtUtc;

        if (normalizedWaId is not null && (isLatest || WaId is null))
        {
            WaId = normalizedWaId;
        }

        if (normalizedProfileName is not null && (isLatest || ProfileName is null))
        {
            ProfileName = normalizedProfileName;
        }

        if (isLatest)
        {
            LastInboundAtUtc = inboundAtUtc;
        }
    }

    /// <summary>
    /// La vincula a la cuenta de la persona: la de su número, o la que el bot acaba de crearle (sección 8 del spec del
    /// ingreso con WhatsApp). Una cuenta tiene un solo contacto: si tenía otro, quien llama lo desvincula antes con
    /// <see cref="UnlinkUser"/>.
    /// </summary>
    public void LinkUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A WhatsApp contact is linked to an account that exists.", nameof(userId));
        }

        UserId = userId;
    }

    /// <summary>
    /// La deja sin cuenta, porque la cuenta pasó a tener otro contacto o ya no tiene este número (lo cambió o lo
    /// desvinculó).
    /// </summary>
    public void UnlinkUser()
    {
        UserId = null;
    }

    private static string? NormalizeWaId(string? waId)
    {
        if (string.IsNullOrWhiteSpace(waId))
        {
            return null;
        }

        return IsValidWaId(waId)
            ? waId
            : throw new ArgumentException($"A wa_id has only digits, up to {MaxWaIdLength}.", nameof(waId));
    }

    private static string? NormalizeUserIdentifier(string? userIdentifier)
    {
        if (string.IsNullOrWhiteSpace(userIdentifier))
        {
            return null;
        }

        return IsValidUserIdentifier(userIdentifier)
            ? userIdentifier
            : throw new ArgumentException(
                $"A BSUID has only visible ASCII characters, up to {MaxUserIdentifierLength}.", nameof(userIdentifier));
    }

    // El nombre lo elige la persona: se limpia y se recorta en lugar de rechazar el mensaje. Se limpia antes de sacar
    // los espacios, porque un carácter nulo puede esconder espacios en los bordes.
    private static string? NormalizeProfileName(string? profileName) =>
        profileName is null ? null : WhatsAppText.Clean(WhatsAppText.Sanitize(profileName).Trim(), MaxProfileNameLength);
}
