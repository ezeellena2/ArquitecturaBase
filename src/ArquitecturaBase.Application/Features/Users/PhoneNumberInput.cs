namespace ArquitecturaBase.Application.Features.Users;

/// <summary>
/// El número de WhatsApp como lo carga un administrador en el alta o en la edición: el país elegido y el número tal como
/// lo escribió, igual que en el ingreso. Sin número, es como si no hubiera venido: el formulario manda el país aunque el
/// campo quede vacío.
/// </summary>
public sealed record PhoneNumberInput(string? Country, string? Number)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Number);
}
