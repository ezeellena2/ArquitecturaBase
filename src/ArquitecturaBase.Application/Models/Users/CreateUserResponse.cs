namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// El alta de un administrador: el id de la cuenta y si fue una borrada que se restauró (N4), para que la pantalla diga
/// «restaurado» y no «creado». Una restaurada conserva su historia, pero sólo con el correo y el número del alta.
/// </summary>
public sealed record CreateUserResponse(Guid Id, bool Restored);
