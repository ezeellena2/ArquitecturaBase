namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>País elegido y número escrito para un alta o una edición administrativa.</summary>
public sealed record PhoneNumberHttpRequest(string? Country, string? Number)
{
    public override string ToString() => nameof(PhoneNumberHttpRequest);
}
