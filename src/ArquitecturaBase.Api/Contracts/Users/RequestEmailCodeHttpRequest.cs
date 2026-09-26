namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>El cuerpo de POST /api/me/email/code: el correo que la persona quiere agregar a su propia cuenta.</summary>
public sealed record RequestEmailCodeHttpRequest(string? Email)
{
    // MVC registra los argumentos de la acción con ToString(): no revelar el destino del código.
    public override string ToString() => nameof(RequestEmailCodeHttpRequest);
}
