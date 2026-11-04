namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// Lo que respondió un servicio de ARCA a una familia: el valor cuando el estado es
/// <see cref="ArcaServiceCheckStatus.Ready"/>, y si no, el estado y un motivo estable. Nunca lleva una excepción.
/// </summary>
public sealed record ArcaServiceReply<T>(ArcaServiceCheckStatus Status, T? Value, string? FailureCode);

public static class ArcaServiceReply
{
    public static ArcaServiceReply<T> Ready<T>(T value) => new(ArcaServiceCheckStatus.Ready, value, null);

    public static ArcaServiceReply<T> Failed<T>(ArcaServiceCheckStatus status, string? failureCode = null) => new(status, default, failureCode);
}
