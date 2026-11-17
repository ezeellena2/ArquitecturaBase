using System.Reflection;
using StackExchange.Redis;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Caching;

/// <summary>
/// Una conexión a Redis cuyos comandos fallan como el que esperaba cuando el cliente soltó un socket colgado («Dead socket
/// detected»): con la excepción de su pipe, no con una <see cref="RedisConnectionException"/>. Pasó con Redis pausado
/// (RedisFailureTests) y es una carrera; acá se repite siempre.
/// </summary>
/// <remarks>
/// Es un <see cref="DispatchProxy"/> para no repetir a mano los métodos de las interfaces del cliente. Es pública y no
/// está sellada porque el proxy se arma heredando de ella.
/// </remarks>
public class DroppedSocketRedis : DispatchProxy
{
    private static readonly MethodInfo FromException = typeof(Task).GetMethods()
        .Single(method => method.Name == nameof(Task.FromException) && method.IsGenericMethodDefinition);

    private IDatabase? _database;

    /// <summary>La conexión: <c>GetDatabase</c> da la base cuyos comandos fallan; el resto no se usa.</summary>
    public static IConnectionMultiplexer Create()
    {
        var connection = DispatchProxy.Create<IConnectionMultiplexer, DroppedSocketRedis>();
        ((DroppedSocketRedis)(object)connection)._database = DispatchProxy.Create<IDatabase, DroppedSocketRedis>();

        return connection;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.Name == nameof(IConnectionMultiplexer.GetDatabase) && _database is not null)
        {
            return _database;
        }

        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task))
        {
            return Task.FromException(ReaderCompleted());
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return FromException.MakeGenericMethod(returnType.GetGenericArguments()[0]).Invoke(null, [ReaderCompleted()]);
        }

        throw new NotSupportedException(targetMethod.Name);
    }

    private static InvalidOperationException ReaderCompleted() => new("Reading is not allowed after reader was completed.");
}
