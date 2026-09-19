namespace ArquitecturaBase.Domain.Results;

public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    Unauthorized = 2,
    Forbidden = 3,
    NotFound = 4,
    Conflict = 5,
    TooManyRequests = 6,
}
