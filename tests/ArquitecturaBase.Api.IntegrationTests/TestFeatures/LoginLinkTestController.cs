using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.LoginLinks;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>
/// POST /test/login-links con <c>{ userId }</c>: devuelve la URL del enlace, con el token en el fragmento, como la
/// mandaría el bot al chat.
/// </summary>
[ApiController]
[Route("test/login-links")]
public sealed class LoginLinkTestController(ILoginLinkTestService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Issue(
        [FromBody] IssueLoginLinkRequest request,
        CancellationToken cancellationToken) =>
        (await service.IssueAsync(request, cancellationToken)).ToActionResult(this);
}
