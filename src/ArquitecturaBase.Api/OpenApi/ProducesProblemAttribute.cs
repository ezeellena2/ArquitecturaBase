using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.OpenApi;

/// <summary>
/// Declara en el documento de OpenAPI una respuesta de error con el ProblemDetails de la Api
/// (<c>application/problem+json</c>). Es solo metadata: no cambia lo que responde la acción. Los errores que se deducen
/// de la firma los agrega <see cref="ProblemResponsesConvention"/>; con este atributo se declaran los demás.
/// </summary>
public sealed class ProducesProblemAttribute(int statusCode)
    : ProducesResponseTypeAttribute(typeof(ProblemDetails), statusCode, ProblemContentType)
{
    private const string ProblemContentType = "application/problem+json";
}
