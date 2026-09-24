using System.Reflection;
using System.Runtime.ExceptionServices;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// El IdentityService real, con una lectura vieja: las búsquedas de un número o de un correo dicen que no es de
/// nadie, ni de una cuenta activa ni de una borrada, aunque ya lo sea. Es lo que ve un pedido cuando otra cuenta se
/// queda con el número justo entre su búsqueda y su guardado: así un test llega al choque con el índice único sin
/// depender de cómo se crucen dos pedidos. Todo lo demás pasa tal cual al servicio real.
/// </summary>
/// <remarks>
/// Es un <see cref="DispatchProxy"/> para no repetir a mano los cuarenta métodos de la interfaz. Es pública y no está
/// sellada porque el proxy se arma heredando de ella.
/// </remarks>
public class StaleIdentityReads : DispatchProxy
{
    private static readonly string[] HiddenLookups =
    [
        nameof(IIdentityService.FindByPhoneAsync),
        nameof(IIdentityService.IsDeletedPhoneAsync),
        nameof(IIdentityService.FindByEmailAsync),
        nameof(IIdentityService.IsDeletedEmailAsync),
    ];

    private IIdentityService _inner = null!;
    private object _hidden = null!;

    /// <summary>
    /// Cambia el servicio de la Api por el real con la lectura vieja de <paramref name="hidden"/>, un
    /// <see cref="PhoneNumber"/> o un <see cref="Email"/>.
    /// </summary>
    public static void Replace(IServiceCollection services, object hidden)
    {
        ArgumentNullException.ThrowIfNull(hidden);

        services.RemoveAll<IIdentityService>();
        services.AddScoped(serviceProvider =>
        {
            var proxy = Create<IIdentityService, StaleIdentityReads>();
            var reads = (StaleIdentityReads)(object)proxy;
            reads._inner = ActivatorUtilities.CreateInstance<IdentityService>(serviceProvider);
            reads._hidden = hidden;

            return proxy;
        });
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (args is [{ } first, ..] && first.Equals(_hidden) && HiddenLookups.Contains(targetMethod.Name, StringComparer.Ordinal))
        {
            return targetMethod.ReturnType == typeof(Task<bool>) ? Task.FromResult(false) : Task.FromResult<UserAccount?>(null);
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(exception.InnerException);

            throw;
        }
    }
}
