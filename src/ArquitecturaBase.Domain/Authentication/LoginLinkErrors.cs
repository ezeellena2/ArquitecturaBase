using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

public static class LoginLinkErrors
{
    public const string InvalidCode = "Auth.LoginLink.Invalid";
    public const string TooManyRequestsCode = "Auth.LoginLink.TooManyRequests";

    /// <summary>La misma clave que los límites de los códigos: el front lee siempre "retryAfter".</summary>
    public const string RetryAfterKey = LoginCodeErrors.RetryAfterKey;

    /// <summary>
    /// El enlace no sirve: vencido, usado, invalidado por uno más nuevo o inventado. Es el mismo error para todos, sin
    /// metadata, a propósito (sección 6.4 del spec del ingreso con WhatsApp): la respuesta no dice si el enlace existió
    /// ni qué le pasó. "Deshabilitada" o "bloqueada" se dice recién después de un enlace válido.
    /// </summary>
    public static readonly Error Invalid = Error.Validation(InvalidCode, "The login link is not valid.");

    /// <summary>
    /// Se pidieron demasiados enlaces para la cuenta: uno por minuto y 5 cada 15 minutos (sección 13 del spec del
    /// ingreso con WhatsApp). Hoy lo recibe el bot, que se lo dice a la persona en el chat.
    /// </summary>
    public static Error TooManyRequests(int retryAfterSeconds) =>
        Error.TooManyRequests(
            TooManyRequestsCode,
            "Too many login links were requested.",
            new Dictionary<string, object?> { [RetryAfterKey] = retryAfterSeconds });
}
