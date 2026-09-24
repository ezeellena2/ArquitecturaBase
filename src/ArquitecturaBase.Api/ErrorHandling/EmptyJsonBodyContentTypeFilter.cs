using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ArquitecturaBase.Api.ErrorHandling;

internal sealed class EmptyJsonBodyContentTypeFilter : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var request = context.HttpContext.Request;
        if (request.ContentType is not null
            || !context.ActionDescriptor.Parameters.Any(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body))
        {
            return;
        }

        var canHaveBody = context.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody;
        if (canHaveBody is false || request.ContentLength is 0)
        {
            // MVC interpreta una solicitud sin Content-Type como 415 antes de comprobar que falta el cuerpo.
            // Las rutas JSON existentes devuelven 400 para ese caso; no se alteran solicitudes con cuerpo.
            request.ContentType = "application/json";
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
