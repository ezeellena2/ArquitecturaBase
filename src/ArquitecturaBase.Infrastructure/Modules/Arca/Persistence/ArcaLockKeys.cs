using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence;

/// <summary>
/// Las claves de los locks consultivos del módulo. Toda escritura toma la de la conexión, también las de las familias; la
/// del ticket ordena su renovación entre hosts y va después de la de la conexión cuando se toman las dos.
/// </summary>
internal static class ArcaLockKeys
{
    public static string Connection => "arca-connection:installation";

    public static string Ticket(Guid certificateId, ArcaServiceCode service) => $"arca-ticket:{certificateId:N}:{service.Value}";
}
