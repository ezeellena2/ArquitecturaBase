namespace ArquitecturaBase.Application.Models.Users;

/// <summary>El número que la persona quiere vincular a su cuenta, tal como lo escribió, y el país elegido.</summary>
public sealed record RequestPhoneLinkCodeRequest(string? Country, string? Number)
{
    // MVC registra argumentos de acción con ToString(): no revelar el número.
    public override string ToString() => nameof(RequestPhoneLinkCodeRequest);
}
