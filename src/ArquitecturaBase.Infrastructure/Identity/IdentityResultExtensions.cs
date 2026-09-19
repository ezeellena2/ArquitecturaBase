using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

internal static class IdentityResultExtensions
{
    /// <summary>
    /// Los casos de uso validan antes de llamar a Identity: si igual rechaza, es un error de programación. El mensaje
    /// lleva solo los códigos de error de Identity, nunca datos del usuario.
    /// </summary>
    public static void EnsureSucceeded(this IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not {action}: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
        }
    }
}
