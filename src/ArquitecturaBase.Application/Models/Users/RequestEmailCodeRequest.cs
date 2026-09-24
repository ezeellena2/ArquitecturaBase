namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Correo que la persona quiere agregar a su propia cuenta.</summary>
public sealed record RequestEmailCodeRequest(string? Email)
{
    // MVC registra los argumentos de la acción con ToString(): no revelar el destino del código.
    public override string ToString() => nameof(RequestEmailCodeRequest);
}
