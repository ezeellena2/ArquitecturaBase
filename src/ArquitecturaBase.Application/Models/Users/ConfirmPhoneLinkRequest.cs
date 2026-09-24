namespace ArquitecturaBase.Application.Models.Users;

/// <summary>El número internacional y el código recibido por WhatsApp para vincularlo a la cuenta.</summary>
public sealed record ConfirmPhoneLinkRequest(string? Phone, string? Code)
{
    // MVC registra argumentos de acción con ToString(): nunca revelar el número ni el código.
    public override string ToString() => nameof(ConfirmPhoneLinkRequest);
}
