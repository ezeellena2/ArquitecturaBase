namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// Los dos secretos del webhook que comparte Meta (sección 7 del spec del ingreso con WhatsApp), comparados en tiempo
/// constante. Nunca dicen por qué algo no coincide, y nada de lo que reciben termina en un log.
/// </summary>
public interface IWhatsAppSignatureValidator
{
    /// <summary>
    /// Si <paramref name="signatureHeader"/> (<c>X-Hub-Signature-256: sha256=&lt;hex&gt;</c>) es el HMAC-SHA256 del
    /// cuerpo crudo con el secreto de la app. Sin encabezado, o con uno mal formado, es falso.
    /// </summary>
    bool IsValidSignature(ReadOnlySpan<byte> body, string? signatureHeader);

    /// <summary>Si es la palabra de verificación que Meta manda al suscribir el webhook (<c>hub.verify_token</c>).</summary>
    bool IsValidVerifyToken(string? verifyToken);
}
