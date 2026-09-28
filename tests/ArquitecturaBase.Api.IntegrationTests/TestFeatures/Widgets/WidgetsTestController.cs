using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Common.Pagination;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

/// <summary>Rutas que existen solo en los tests de integración.</summary>
[ApiController]
[Route("test/widgets")]
public sealed class WidgetsTestController(IWidgetTestService widgets) : ControllerBase
{
    [HttpPost("")]
    public async Task<IActionResult> CreateWidget(
        [FromBody] CreateWidgetRequest request,
        CancellationToken cancellationToken) =>
        (await widgets.CreateAsync(request, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetWidget(
        Guid id,
        CancellationToken cancellationToken) =>
        (await widgets.GetWidgetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpGet("")]
    public async Task<IActionResult> GetWidgets(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        (await widgets.ListWidgetsAsync(
            new ListWidgetsRequest
            {
                Page = page ?? PagedRequest.DefaultPage,
                PageSize = pageSize ?? PagedRequest.DefaultPageSize,
                Sort = sort,
                Search = search,
            },
            cancellationToken)).ToActionResult(this);
}
