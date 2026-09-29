using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

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
///   <item><description>429, si tiene un límite de pedidos (<c>[EnableRateLimiting]</c> en el controller o en la acción,
///   sin un <c>[DisableRateLimiting]</c> más cercano que lo anule): el rate limiter responde su propio ProblemDetails
///   con retryAfter.</description></item>
/// </list>
/// Lo que no se deduce de la firma lo declara el controller o la acción con <see cref="ProducesProblemAttribute"/> (por
/// ejemplo, el 404 de <c>/api/me</c>, cuya cuenta puede haberse borrado, los 409 de un conflicto, el 403 de una acción
/// anónima que rechaza una cuenta desactivada o el 429 de un servicio sin rate limit), y la convención no repite un
/// status que ya está declarado. Es solo metadata para el documento: no cambia lo que responde ninguna acción.
/// </summary>
/// <remarks>
/// La respuesta de éxito la declara cada acción con <c>[ProducesResponseType&lt;T&gt;]</c> (o el 204 sin tipo). Una
/// acción que no la declare queda en el documento solo con sus errores, porque ApiExplorer deja de suponer el 200 en
/// cuanto hay alguna respuesta declarada: OpenApiTests lo detecta en las rutas de <c>/api</c>.
/// </remarks>
internal sealed class ProblemResponsesConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        // Los que hablan otro protocolo (OpenIddict, Google, el webhook) llevan [OwnProtocol] y declaran a mano lo suyo.
        foreach (var controller in application.Controllers.Where(controller =>
            !controller.ControllerType.IsDefined(typeof(OwnProtocolAttribute), inherit: true)))
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

        // Como en la metadata del endpoint, gana el más cercano: el de la acción sobre el del controller.
        if (attributes.LastOrDefault(attribute => attribute is EnableRateLimitingAttribute or DisableRateLimitingAttribute)
            is EnableRateLimitingAttribute)
        {
            yield return StatusCodes.Status429TooManyRequests;
        }

        yield return StatusCodes.Status500InternalServerError;
    }
}
