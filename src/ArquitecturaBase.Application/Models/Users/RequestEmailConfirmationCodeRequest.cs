namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// El pedido, desde el perfil, del código que el correo verificado de la cuenta manda para confirmar un cambio del
/// número (cada medio confirma los cambios del otro, 2026-11-16). El cambio decide qué dice el correo.
/// </summary>
public sealed record RequestEmailConfirmationCodeRequest(ProfileChange? Change);

/// <summary>Los cambios del número que confirma un código al correo verificado de la cuenta.</summary>
public enum ProfileChange
{
    /// <summary>Vincular un número (o reemplazar uno sin verificar): <c>PUT /api/me/whatsapp</c> con <c>emailCode</c>.</summary>
    LinkPhone = 1,

    /// <summary>Desvincular el número: <c>DELETE /api/me/whatsapp</c> con <c>code</c>.</summary>
    UnlinkPhone = 2,
}
