namespace ArquitecturaBase.Api.ErrorHandling;

/// <summary>
/// Códigos de los errores que arma la propia Api, sin pasar por un Result: excepciones, binding y los que genera
/// el framework. Son parte del contrato con el front y cada uno tiene su texto en Errors.resx.
/// </summary>
internal static class ApiErrorCodes
{
    public const string Unexpected = "General.Unexpected";
    public const string InvalidRequest = "Request.Invalid";
    public const string Unauthorized = "Http.Unauthorized";
    public const string Forbidden = "Http.Forbidden";
    public const string NotFound = "Http.NotFound";
    public const string MethodNotAllowed = "Http.MethodNotAllowed";
    public const string Conflict = "Http.Conflict";
    public const string TooManyRequests = "Http.TooManyRequests";
}
