using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.SystemQueries;

public static class SystemQueryErrors
{
    public const string ForbiddenCode = "System.Query.Forbidden";
    public const string InvalidArgumentsCode = "System.Query.InvalidArguments";
    public const string NotFoundCode = "System.Query.NotFound";
    public const string UnavailableCode = "System.Query.Unavailable";
    public static readonly Error Forbidden = Error.Forbidden(ForbiddenCode, "The current account cannot access this source.");
    public static readonly Error InvalidArguments = Error.Validation(InvalidArgumentsCode, "The query arguments are invalid.");
    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The requested source item was not found.");
    // I5: the source's database failed (not a cancellation); an engine read is unavailable and a proposal ends with it.
    public static readonly Error Unavailable = Error.Failure(UnavailableCode, "The source is temporarily unavailable.");
}
