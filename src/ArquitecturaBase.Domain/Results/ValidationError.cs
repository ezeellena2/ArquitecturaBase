namespace ArquitecturaBase.Domain.Results;

/// <summary>Error de validación con los mensajes agrupados por campo (campo → mensajes).</summary>
public sealed record ValidationError : Error
{
    public const string ErrorCode = "Validation.Failed";

    public ValidationError(IReadOnlyDictionary<string, string[]> errors)
        : base(ErrorCode, "One or more validation errors occurred.", ErrorType.Validation)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
