using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// Con WhatsApp prendido, lo que el envío necesita se controla al arrancar: si no, la primera señal sería el primer
/// código que no llega. Los mensajes nombran la clave, y el del token se basta solo: trae el comando entero para
/// cargarlo, listo para copiar, sin mandar a buscarlo a otro documento.
/// </summary>
internal sealed partial class WhatsAppOptionsValidator : IValidateOptions<WhatsAppOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AccessToken))
        {
            failures.Add(
                "Missing WhatsApp:AccessToken, the system user token that sends the messages. In development, load it "
                + """from the repository root with: dotnet user-secrets set "WhatsApp:AccessToken" "<token>" --project src/ArquitecturaBase.Api""");
        }

        if (options.GraphApiVersion is null || !GraphApiVersionFormat().IsMatch(options.GraphApiVersion))
        {
            failures.Add($"WhatsApp:GraphApiVersion must look like v25.0 (it is '{options.GraphApiVersion}').");
        }

        if (string.IsNullOrWhiteSpace(options.Templates?.LoginCode))
        {
            failures.Add("Missing WhatsApp:Templates:LoginCode, the name of the authentication template approved in Meta.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex(@"^v\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex GraphApiVersionFormat();
}
