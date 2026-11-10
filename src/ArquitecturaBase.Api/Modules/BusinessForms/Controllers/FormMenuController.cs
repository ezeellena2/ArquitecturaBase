using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

// CP5-A: the folders in the side menu. Any active account reads it; whoever cannot open forms gets it empty.
[ApiController]
[Route("api/forms/menu")]
[Tags("Form folders")]
[Authorize]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormMenuController(IFormMenuService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FormMenuResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await service.GetAsync(cancellationToken)).ToActionResult(this);
}
