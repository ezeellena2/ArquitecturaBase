using System.Text;
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.Arca.Contracts.Connection;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.Arca.Controllers;

/// <summary>
/// La conexión con ARCA: leerla pide <c>arca.read</c>; cambiarla o llamar a ARCA, además <c>arca.manage</c>. Subir el
/// certificado también lo prueba, como pide el alta: responde el resultado de la prueba.
/// </summary>
[ApiController]
[Route("api/arca/connection")]
[Tags("ARCA")]
[HasPermission(ArcaPermissions.Read)]
[EnableRateLimiting(ArcaApiRegistration.ReadPolicy)]
[RequestSizeLimit(16384)]
public sealed class ArcaConnectionController(IArcaConnectionService service, IArcaConnectionTestService tester) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ArcaConnectionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => (await service.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpPost("certificate-requests")]
    [HasPermission(ArcaPermissions.Manage)]
    [EnableRateLimiting(ArcaApiRegistration.WritePolicy)]
    [ProducesResponseType<ArcaConnectionResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartCertificateRequest([FromBody] StartArcaConnectionHttpRequest request, CancellationToken cancellationToken) =>
        (await service.StartCertificateRequestAsync(request.Cuit, request.Environment, request.Alias, request.Services ?? [],
            request.ExpectedRevision, request.Mode ?? ArcaAccessMode.CompanyCertificate, cancellationToken)).ToActionResult(this);

    [HttpGet("certificate-requests/current")]
    [HasPermission(ArcaPermissions.Manage)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pkcs10")]
    public async Task<IActionResult> GetPendingRequest(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await service.GetPendingRequestAsync(cancellationToken);
        return result.IsFailure ? result.ToActionResult(this)
            : File(Encoding.ASCII.GetBytes(result.Value.RequestPem), "application/pkcs10", $"arca-{result.Value.Alias}.csr");
    }

    [HttpDelete("certificate-requests/current")]
    [HasPermission(ArcaPermissions.Manage)]
    [EnableRateLimiting(ArcaApiRegistration.WritePolicy)]
    [ProducesResponseType<ArcaConnectionResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelRenewal([FromQuery] long expectedRevision, CancellationToken cancellationToken) =>
        (await service.CancelRenewalAsync(expectedRevision, cancellationToken)).ToActionResult(this);

    [HttpPut("services")]
    [HasPermission(ArcaPermissions.Manage)]
    [EnableRateLimiting(ArcaApiRegistration.WritePolicy)]
    [ProducesResponseType<ArcaConnectionResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChooseServices([FromBody] ChooseArcaServicesHttpRequest request, CancellationToken cancellationToken) =>
        (await service.ChooseServicesAsync(request.Services ?? [], request.ExpectedRevision, cancellationToken)).ToActionResult(this);

    [HttpPut("certificate")]
    [HasPermission(ArcaPermissions.Manage)]
    [EnableRateLimiting(ArcaApiRegistration.RemotePolicy)]
    [ProducesResponseType<ArcaTestResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadCertificate([FromBody] UploadArcaCertificateHttpRequest request, CancellationToken cancellationToken)
    {
        var uploaded = await service.UploadCertificateAsync(request.Certificate, request.ExpectedRevision, cancellationToken);
        return uploaded.IsFailure ? uploaded.ToActionResult(this) : (await tester.TestAsync(cancellationToken)).ToActionResult(this);
    }

    [HttpPost("test")]
    [HasPermission(ArcaPermissions.Manage)]
    [EnableRateLimiting(ArcaApiRegistration.RemotePolicy)]
    [ProducesResponseType<ArcaTestResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Test(CancellationToken cancellationToken) => (await tester.TestAsync(cancellationToken)).ToActionResult(this);

    [HttpDelete]
    [HasPermission(ArcaPermissions.Manage)]
    [EnableRateLimiting(ArcaApiRegistration.WritePolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Disconnect([FromQuery] long expectedRevision, CancellationToken cancellationToken) =>
        (await service.DisconnectAsync(expectedRevision, cancellationToken)).ToActionResult(this);
}
