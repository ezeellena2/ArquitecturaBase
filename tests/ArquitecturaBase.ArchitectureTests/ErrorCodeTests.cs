using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ArquitecturaBase.ArchitectureTests;

public sealed partial class ErrorCodeTests
{
    // Claves de Errors.resx que no son códigos de negocio (Area.Entidad.Motivo) y tienen su propia forma.
    private static readonly string[] ReservedKeys =
    [
        // Títulos de ProblemDetails: ErrorMessages.Title arma "Title." + ErrorType, y ProblemDetailsMapper usa además
        // el del 405, que no tiene ErrorType.
        "Title.Failure",
        "Title.Validation",
        "Title.Unauthorized",
        "Title.Forbidden",
        "Title.NotFound",
        "Title.Conflict",
        "Title.TooManyRequests",
        "Title.MethodNotAllowed",

        // El código común de validación (ValidationError.ErrorCode en Domain).
        "Validation.Failed",

        // ApiErrorCodes: los errores que arma la propia Api sin pasar por un Result (excepciones, binding y los que
        // genera el framework). Son parte del contrato con el front.
        "General.Unexpected",
        "Request.Invalid",
        "Http.Unauthorized",
        "Http.Forbidden",
        "Http.NotFound",
        "Http.MethodNotAllowed",
        "Http.Conflict",
        "Http.TooManyRequests",
    ];

    [Fact]
    public void Error_codes_follow_the_area_entity_reason_format()
    {
        var malformed = ReadErrorKeys()
            .Where(key => !ReservedKeys.Contains(key, StringComparer.Ordinal))
            .Where(key => !ErrorCodeFormat().IsMatch(key));

        Assert.Empty(malformed);
    }

    [Fact]
    public void Reserved_error_keys_still_exist_in_the_resource()
    {
        // Una excepción que ya no está en el resx queda como puerta abierta para una clave mal formada: hay que sacarla.
        Assert.Empty(ReservedKeys.Except(ReadErrorKeys(), StringComparer.Ordinal));
    }

    private static string[] ReadErrorKeys() =>
        XDocument.Load(Path.Combine(SolutionRoot.FullPath, "src", "ArquitecturaBase.Application", "Resources", "Errors.resx"))
            .Root!
            .Elements("data")
            .Select(data => data.Attribute("name")!.Value)
            .ToArray();

    // Area.Entidad.Motivo: tres segmentos o más, cada uno en PascalCase (por ejemplo, Auth.LoginCode.Expired).
    [GeneratedRegex(@"^[A-Z][A-Za-z]+(\.[A-Z][A-Za-z0-9]+){2,}$")]
    private static partial Regex ErrorCodeFormat();
}
