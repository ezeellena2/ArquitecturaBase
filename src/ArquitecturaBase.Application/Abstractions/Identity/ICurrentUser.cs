namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>Usuario de la petición actual. Sin sesión, <see cref="UserId"/> es null.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }
}
