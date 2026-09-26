using ArquitecturaBase.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ArquitecturaBase.Api.OpenApi;

/// <summary>
/// Declara en el documento de OpenAPI los errores de cada acción, todos como ProblemDetails
/// (<see cref="ProducesProblemAttribute"/>). Las reglas salen de la firma de la acción, sin mirar el servicio:
/// <list type="bullet">
///   <item><description>500, siempre: un bug o una falla de infraestructura los responde GlobalExceptionHandler.</description></item>
///   <item><description>400, si recibe un cuerpo o una query: MVC responde 400 cuando no los puede leer, y los
///   validadores, cuando no son válidos.</description></item>
///   <item><description>401, si pide sesión (<c>[Authorize]</c> o <c>[HasPermission]</c>) y no es
///   <c>[AllowAnonymous]</c>. Una acción anónima no declara ni 401 ni 403.</description></item>
///   <item><description>403, si además pide un permiso: la sesión sola no alcanza. Un <c>[Authorize]</c> sin política
///   solo pide sesión y nunca responde 403.</description></item>
///   <item><description>404, si la ruta nombra un recurso (<c>{id}</c>), que puede no existir.</description></item>
/// </list>
/// Lo que no se deduce de la firma lo declara el controller o la acción con <see cref="ProducesProblemAttribute"/> (por
/// ejemplo, el 404 de <c>/api/me</c>, cuya cuenta puede haberse borrado), y la convención no repite un status que ya
/// está declarado. Es solo metadata para el documento: no cambia lo que responde ninguna acción.
/// </summary>
/// <remarks>
/// La respuesta de éxito la declara cada acción con <c>[ProducesResponseType&lt;T&gt;]</c> (o el 204 sin tipo). Una
/// acción que no la declare queda en el documento solo con sus errores, porque ApiExplorer deja de suponer el 200 en
/// cuanto hay alguna respuesta declarada: OpenApiTests lo detecta en las rutas de <c>/api</c>.
/// </remarks>
internal sealed class ProblemResponsesConvention : IApplicationModelConvention
{
    /// <summary>
    /// No siguen estas reglas porque hablan otro protocolo: <c>/connect</c> responde los errores de OAuth de OpenIddict,
    /// el webhook le contesta a Meta y el ingreso con Google responde con navegaciones del navegador, así que declara a
    /// mano lo suyo.
    /// </summary>
    private static readonly HashSet<Type> OwnProtocolControllers =
    [
        typeof(ConnectController),
        typeof(ExternalLoginController),
        typeof(WhatsAppWebhookController),
    ];

    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var controller in application.Controllers.Where(controller =>
            !OwnProtocolControllers.Contains(controller.ControllerType.AsType())))
        {
            foreach (var action in controller.Actions)
            {
                var declared = controller.Filters.Concat(action.Filters)
                    .OfType<IApiResponseMetadataProvider>()
                    .Select(response => response.StatusCode)
                    .ToHashSet();

                foreach (var statusCode in ErrorStatusCodes(controller, action).Where(statusCode => !declared.Contains(statusCode)))
                {
                    action.Filters.Add(new ProducesProblemAttribute(statusCode));
                }
            }
        }
    }

    private static IEnumerable<int> ErrorStatusCodes(ControllerModel controller, ActionModel action)
    {
        var sources = action.Parameters.Select(parameter => parameter.BindingInfo?.BindingSource).ToArray();
        var attributes = controller.Attributes.Concat(action.Attributes).ToArray();
        var authorization = attributes.OfType<IAuthorizeData>().ToArray();

        if (sources.Any(source => source == BindingSource.Body || source == BindingSource.Query))
        {
            yield return StatusCodes.Status400BadRequest;
        }

        if (authorization.Length > 0 && !attributes.OfType<IAllowAnonymous>().Any())
        {
            yield return StatusCodes.Status401Unauthorized;

            if (authorization.Any(data => !string.IsNullOrEmpty(data.Policy) || !string.IsNullOrEmpty(data.Roles)))
            {
                yield return StatusCodes.Status403Forbidden;
            }
        }

        if (sources.Any(source => source == BindingSource.Path))
        {
            yield return StatusCodes.Status404NotFound;
        }

        yield return StatusCodes.Status500InternalServerError;
    }
}
