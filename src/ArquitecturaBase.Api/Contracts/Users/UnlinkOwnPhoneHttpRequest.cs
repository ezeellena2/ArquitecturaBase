namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>
/// El cuerpo de DELETE /api/me/whatsapp: con un correo verificado, el código que llegó a ese correo para confirmar que se
/// desvincula el número (N11). Sin correo verificado puede faltar.
/// </summary>
public sealed record UnlinkOwnPhoneHttpRequest(string? Code)
{
    // MVC registra los argumentos de la acción con ToString(): nunca revelar el código.
    public override string ToString() => nameof(UnlinkOwnPhoneHttpRequest);
}
