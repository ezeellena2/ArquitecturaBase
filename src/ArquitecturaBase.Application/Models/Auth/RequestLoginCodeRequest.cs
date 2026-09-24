namespace ArquitecturaBase.Application.Models.Auth;

public sealed record RequestLoginCodeRequest(string? Email)
{
    public override string ToString() => nameof(RequestLoginCodeRequest);
}
