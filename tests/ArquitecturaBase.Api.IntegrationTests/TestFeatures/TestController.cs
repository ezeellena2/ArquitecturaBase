using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>Rutas que existen solo en los tests de integración.</summary>
[ApiController]
[Route("test")]
public sealed class TestController : ControllerBase
{
    [HttpGet("boom")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "MVC actions must be instance methods.")]
    public IActionResult Boom() =>
        throw new InvalidOperationException("Sensitive detail that must never reach the client.");

    [HttpPost("dates")]
    public IActionResult EchoDate([FromBody] DateEchoRequest request) => Ok(request);

    /// <summary>El tope de la subida de prueba: más que el global de Kestrel, como las rutas que suben archivos.</summary>
    public const long UploadLimitBytes = 4 * 1024 * 1024;

    // Una subida con su propio tope (N6): [RequestSizeLimit] manda sobre el tope global de Kestrel.
    [HttpPost("upload")]
    [RequestSizeLimit(UploadLimitBytes)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);

        return Ok(new { buffer.Length });
    }

    // Errores que arma el framework, sin pasar por Result: 401/403 de la autorización y respuestas vacías
    // con cualquier código (simulan, por ejemplo, el 429 del rate limiter).
    [Authorize]
    [HttpGet("protected")]
    public IActionResult Protected() => NoContent();

    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public IActionResult Admin() => NoContent();

    [HttpGet("status/{statusCode:int}")]
    public IActionResult EmptyStatus(int statusCode) => StatusCode(statusCode);

    // Verifica el pipeline real de ForwardedHeaders sin publicar un endpoint de diagnóstico en la Api.
    [HttpGet("request-context")]
    public IActionResult RequestContext() => Ok(new
    {
        Request.Scheme,
        RemoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        Host = Request.Host.Value,
    });
}
