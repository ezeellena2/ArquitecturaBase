using System.Reflection;
using System.Runtime.ExceptionServices;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence.Readers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// El IdentityService y el UserReader reales, con una lectura vieja: las búsquedas de un número o de un correo dicen
/// que no es de nadie, ni de una cuenta activa ni de una borrada, aunque ya lo sea. Es lo que ve un pedido cuando otra
/// cuenta se queda con el número justo entre su búsqueda y su guardado: así un test llega al choque con el índice único
/// sin depender de cómo se crucen dos pedidos. Todo lo demás pasa tal cual al servicio real.
/// </summary>
/// <remarks>
/// Es un <see cref="DispatchProxy"/> para no repetir a mano los métodos de las dos interfaces. Es pública y no está
/// sellada porque el proxy se arma heredando de ella. Tapa las dos puertas: Application lee las cuentas por
/// <see cref="IUserReader"/> (el alta, la edición y el perfil) y por <see cref="IIdentityService"/> (el ingreso). Si
/// quedara una abierta, el 409 saldría del chequeo previo sin pasar por el choque ni por el savepoint.
/// </remarks>
public class StaleIdentityReads : DispatchProxy
{
    private static readonly string[] HiddenLookups =
    [
        nameof(IUserReader.FindByPhoneAsync),
        nameof(IUserReader.IsDeletedPhoneAsync),
        nameof(IUserReader.FindByEmailAsync),
        nameof(IUserReader.IsDeletedEmailAsync),
    ];

    private object _inner = null!;
    private object _hidden = null!;

    /// <summary>
    /// Cambia el UserReader y el IdentityService de la Api por los reales con la lectura vieja de
    /// <paramref name="hidden"/>, un <see cref="PhoneNumber"/> o un <see cref="Email"/>.
    /// </summary>
    public static void Replace(IServiceCollection services, object hidden)
    {
        ArgumentNullException.ThrowIfNull(hidden);

        services.RemoveAll<IUserReader>();
        services.AddScoped(serviceProvider =>
            Wrap<IUserReader>(ActivatorUtilities.CreateInstance<UserReader>(serviceProvider), hidden));

        // El IdentityService real recibe el lector viejo de arriba: las dos puertas dicen lo mismo.
        services.RemoveAll<IIdentityService>();
        services.AddScoped(serviceProvider =>
            Wrap<IIdentityService>(ActivatorUtilities.CreateInstance<IdentityService>(serviceProvider), hidden));
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

    private static TService Wrap<TService>(TService inner, object hidden)
        where TService : class
    {
        var proxy = Create<TService, StaleIdentityReads>();
        var reads = (StaleIdentityReads)(object)proxy;
        reads._inner = inner;
        reads._hidden = hidden;

        return proxy;
    }
}
