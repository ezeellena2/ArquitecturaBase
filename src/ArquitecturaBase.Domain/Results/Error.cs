namespace ArquitecturaBase.Domain.Results;

/// <summary>
/// Error de negocio. <see cref="Code"/> es estable y sigue el formato Area.Entidad.Motivo:
/// la API lo usa como clave para traducir la descripción desde Errors.resx.
/// </summary>
public record Error(string Code, string Description, ErrorType Type, IReadOnlyDictionary<string, object?>? Metadata = null)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Failure, metadata);

    public static Error Validation(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Validation, metadata);

    public static Error Unauthorized(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Unauthorized, metadata);

    public static Error Forbidden(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Forbidden, metadata);

    public static Error NotFound(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.NotFound, metadata);

    public static Error Conflict(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Conflict, metadata);

    public static Error TooManyRequests(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.TooManyRequests, metadata);
}
