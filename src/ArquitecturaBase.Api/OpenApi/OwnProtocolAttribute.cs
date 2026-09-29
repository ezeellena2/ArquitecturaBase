namespace ArquitecturaBase.Api.OpenApi;

/// <summary>
/// Un controller que habla su propio protocolo y no sigue la convención de errores de OpenAPI
/// (<see cref="ProblemResponsesConvention"/>): <c>/connect</c> responde los errores de OAuth de OpenIddict, el ingreso
/// con Google responde con navegaciones del navegador y el webhook de un módulo le contesta a su proveedor. Declara a
/// mano lo suyo. Es un atributo y no una lista, así la convención no nombra los controllers de un módulo.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class OwnProtocolAttribute : Attribute;
