namespace ArquitecturaBase.Api.Contracts.Auth;

/// <summary>El cuerpo de POST /account/login-code: el correo al que se manda el código.</summary>
public sealed record RequestLoginCodeHttpRequest(string? Email)
{
    // MVC registra los argumentos de la acción con ToString(): no revelar el destino del código.
    public override string ToString() => nameof(RequestLoginCodeHttpRequest);
}
