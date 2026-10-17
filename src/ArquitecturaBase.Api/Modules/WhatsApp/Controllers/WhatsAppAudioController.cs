using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.WhatsApp.Routing;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.WhatsApp.Controllers;
[ApiController]
[Route("api/whatsapp/chats/{contactId:guid}/messages/{messageId:guid}")]
[Tags("WhatsApp chats")]
[WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
[HasPermission(WhatsAppPermissions.Read)]
[EnableRateLimiting(WhatsAppApiRegistration.ChatReadRateLimitPolicy)]
[ProducesProblem(StatusCodes.Status404NotFound)]
public sealed class WhatsAppAudioController(IWhatsAppAudioService audio,IWhatsAppAudioContentService content):ControllerBase
{
    [HttpPost("audio-transcription")]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType<WhatsAppAudioResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Transcribe([FromRoute]Guid contactId,[FromRoute]Guid messageId,CancellationToken ct)=>
        (await audio.RequestAsync(contactId,messageId,ct)).ToAcceptedResult(this);

    [HttpGet("audio")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    public async Task<IActionResult> Open([FromRoute]Guid contactId,[FromRoute]Guid messageId,CancellationToken ct)
    {
        var result=await content.OpenAsync(contactId,messageId,ct);
        if(result.IsFailure)return result.ToActionResult(this);
        Response.Headers.CacheControl="no-store";Response.Headers.XContentTypeOptions="nosniff";
        return File(result.Value.Content,result.Value.ContentType,result.Value.Name,enableRangeProcessing:true);
    }
}
