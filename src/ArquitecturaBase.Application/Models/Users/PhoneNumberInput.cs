namespace ArquitecturaBase.Application.Models.Users;

/// <summary>País elegido y número escrito por el administrador.</summary>
public sealed record PhoneNumberInput(string? Country, string? Number)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Number);
}
