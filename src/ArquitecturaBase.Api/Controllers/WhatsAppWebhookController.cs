using System.Buffers;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Api.Routing;
using ArquitecturaBase.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Controllers;

/// <summary>Receives Meta verification and signed webhook notices.</summary>
[ApiController]
[Route("webhooks/whatsapp")]
[Tags("Webhooks")]
[WhatsAppRoute(WhatsAppRouteFeature.Webhook)]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingExtensions.WhatsAppWebhookPolicy)]
public sealed partial class WhatsAppWebhookController(
    IWhatsAppWebhookService service,
    ILogger<WhatsAppWebhookController> logger) : ControllerBase
{
    private const int MaxBodyBytes = 5 * 1024 * 1024;
    private const int ReadChunkBytes = 16 * 1024;
    private const string SignatureHeader = "X-Hub-Signature-256";

    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (string.Equals(mode, "subscribe", StringComparison.Ordinal)
            && service.IsValidVerifyToken(verifyToken)
            && !string.IsNullOrEmpty(challenge))
        {
            LogVerified(logger);
            return Content(challenge, "text/plain");
        }

        LogVerificationRejected(logger);
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        if (await ReadBodyAsync(HttpContext, cancellationToken) is not { } body)
        {
            LogTooLarge(logger, MaxBodyBytes);
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (!await service.ReceiveAsync(body, Request.Headers[SignatureHeader].ToString(), cancellationToken))
        {
            return StatusCode(StatusCodes.Status401Unauthorized);
        }

        return Ok();
    }

    /// <summary>Read the exact signed bytes while enforcing the limit under Kestrel and TestServer.</summary>
    private static async Task<ArraySegment<byte>?> ReadBodyAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;

        if (request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
        {
            sizeLimit.MaxRequestBodySize = MaxBodyBytes;
        }

        // Content-Length is untrusted. Allocate only as bytes actually arrive.
        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(ReadChunkBytes);

        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return buffer.TryGetBuffer(out var written) ? written : buffer.ToArray();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Meta verified the WhatsApp webhook")]
    private static partial void LogVerified(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a WhatsApp webhook verification: wrong mode or verify token")]
    private static partial void LogVerificationRejected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a WhatsApp webhook larger than {MaxBytes} bytes")]
    private static partial void LogTooLarge(ILogger logger, int maxBytes);
}
