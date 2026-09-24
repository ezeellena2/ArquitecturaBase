using System.Diagnostics.CodeAnalysis;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;
using ArquitecturaBase.Application.Common.Pagination;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>Rutas que existen solo en los tests de integración.</summary>
[ApiController]
[Route("test")]
public sealed class TestController(IWidgetTestService widgets) : ControllerBase
{
    [HttpPost("widgets")]
    public async Task<IActionResult> CreateWidget(
        [FromBody] CreateWidgetRequest request,
        CancellationToken cancellationToken) =>
        (await widgets.CreateAsync(request, cancellationToken)).ToActionResult(this);

    [HttpGet("widgets/{id:guid}")]
    public async Task<IActionResult> GetWidget(
        Guid id,
        CancellationToken cancellationToken) =>
        (await widgets.GetByIdAsync(id, cancellationToken)).ToActionResult(this);

    [HttpGet("widgets")]
    public async Task<IActionResult> GetWidgets(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        (await widgets.ListAsync(
            new GetWidgetsRequest
            {
                Page = page ?? PagedRequest.DefaultPage,
                PageSize = pageSize ?? PagedRequest.DefaultPageSize,
                Sort = sort,
                Search = search,
            },
            cancellationToken)).ToActionResult(this);

    [HttpGet("boom")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "MVC actions must be instance methods.")]
    public IActionResult Boom() =>
        throw new InvalidOperationException("Sensitive detail that must never reach the client.");

    [HttpPost("dates")]
    public IActionResult EchoDate([FromBody] DateEchoRequest request) => Ok(request);

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
