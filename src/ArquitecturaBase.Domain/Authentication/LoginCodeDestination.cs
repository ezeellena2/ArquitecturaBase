using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.Authentication;

/// <summary>
/// A dónde va un código de ingreso: un correo o un número de WhatsApp (sección 6.3 del spec del ingreso con WhatsApp).
/// Solo se arma a partir de un <see cref="Email"/> o de un <see cref="PhoneNumber"/>, así que el valor ya llega
/// validado y normalizado: es lo que guarda la fila del código y lo que identifica al destino en el lock y en los
/// límites. A propósito no redefine <c>ToString</c>: un destino que termine en un log no deja el número a la vista.
/// </summary>
public sealed class LoginCodeDestination : ValueObject
{
    private LoginCodeDestination(LoginCodeChannel channel, string value)
    {
        Channel = channel;
        Value = value;
    }

    public LoginCodeChannel Channel { get; }

    /// <summary>El correo normalizado o el número en formato internacional.</summary>
    public string Value { get; }

    public static LoginCodeDestination ForEmail(Email email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return new LoginCodeDestination(LoginCodeChannel.Email, email.Value);
    }

    public static LoginCodeDestination ForPhone(PhoneNumber phone)
    {
        ArgumentNullException.ThrowIfNull(phone);

        return new LoginCodeDestination(LoginCodeChannel.WhatsApp, phone.Value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Channel;
        yield return Value;
    }
}
