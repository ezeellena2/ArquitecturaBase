using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence;

// I9: an upload of Home takes its slots here (AiAssistantUploadLimiter): after the session, the permission and the person's window of
// sends, which the middlewares already checked, and before the model binding reads the body. Refused, the common 429. With them taken, the
// form has UploadBodySeconds to arrive: past that, or if the client cuts, the request is aborted. The model binding takes the form already
// read, also its failure (a body over the route's limit is the same 400 as before), and the slots go back however the request ends.
[AttributeUsage(AttributeTargets.Method)]
public sealed class AiAssistantUploadSlotAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!http.Request.HasFormContentType)
        {
            // Not a form: the binding answers it as always, with no file to read.
            await next();
            return;
        }
        var services = http.RequestServices;
        var personId = services.GetRequiredService<ICurrentUser>().UserId
            ?? throw new InvalidOperationException("An upload slot needs the person of the request.");
        using var lease = await services.GetRequiredService<AiAssistantUploadLimiter>().AcquireAsync(personId, http.RequestAborted);
        if (!lease.IsAcquired)
        {
            await RateLimitingExtensions.WriteRejectionAsync(http, lease);
            context.Result = new EmptyResult();
            return;
        }
        var seconds = services.GetRequiredService<IOptions<AiRateLimitOptions>>().Value.UploadBodySeconds;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds), services.GetRequiredService<TimeProvider>());
        using var body = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, http.RequestAborted);
        try
        {
            await http.Request.ReadFormAsync(body.Token);
        }
        catch (OperationCanceledException) when (body.IsCancellationRequested)
        {
            http.Abort();
            context.Result = new EmptyResult();
            return;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // The form feature keeps this failure: the model binding gets it again and answers it as it always did.
        }
        await next();
    }
}
