using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// Con WhatsApp prendido, lo que el envío y el webhook necesitan se controla al arrancar: si no, la primera señal sería
/// el primer código que no llega. Los mensajes nombran la clave, y los de los secretos se bastan solos: traen el
/// comando entero para cargarlos, listo para copiar, sin mandar a buscarlo a otro documento. Nunca traen un valor.
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

        // Los dos secretos del webhook van juntos. Sin ninguno, el webhook queda apagado a propósito: así arranca el
        // desarrollo hasta que se configura el túnel. Con uno solo, falta el otro.
        var hasAppSecret = !string.IsNullOrWhiteSpace(options.AppSecret);
        var hasVerifyToken = !string.IsNullOrWhiteSpace(options.VerifyToken);

        if (hasVerifyToken && !hasAppSecret)
        {
            failures.Add(
                "Missing WhatsApp:AppSecret, the Meta app secret that signs the webhooks: WhatsApp:VerifyToken is set, and "
                + "the webhook needs both. It is in the Meta app settings, Basic. In development, load it from the "
                + """repository root with: dotnet user-secrets set "WhatsApp:AppSecret" "<app-secret>" --project src/ArquitecturaBase.Api""");
        }

        if (hasAppSecret && !hasVerifyToken)
        {
            failures.Add(
                "Missing WhatsApp:VerifyToken, the word Meta sends to verify the webhook: WhatsApp:AppSecret is set, and the "
                + "webhook needs both. Make up a long random one and load the same one in Meta. In development, load it "
                + """from the repository root with: dotnet user-secrets set "WhatsApp:VerifyToken" "<verify-token>" --project src/ArquitecturaBase.Api""");
        }

        if (options.GraphApiVersion is null || !GraphApiVersionFormat().IsMatch(options.GraphApiVersion))
        {
            failures.Add($"WhatsApp:GraphApiVersion must look like v25.0 (it is '{options.GraphApiVersion}').");
        }

        if (string.IsNullOrWhiteSpace(options.Templates?.LoginCode))
        {
            failures.Add("Missing WhatsApp:Templates:LoginCode, the name of the authentication template approved in Meta.");
        }

        if (string.IsNullOrWhiteSpace(options.Templates?.Invitation))
        {
            failures.Add("Missing WhatsApp:Templates:Invitation, the name of the invitation template approved in Meta.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex(@"^v\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex GraphApiVersionFormat();
}
