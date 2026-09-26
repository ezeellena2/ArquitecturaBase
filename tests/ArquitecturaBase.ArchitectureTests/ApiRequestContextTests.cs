using System.Reflection;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class ApiRequestContextTests
{
    private const string ApiNamespace = "ArquitecturaBase.Api";
    private const string RequestContextNamespace = ApiNamespace + ".RequestContext";
    private const string HttpContextAccessorType = "Microsoft.AspNetCore.Http.IHttpContextAccessor";

    private static readonly Assembly ApiAssembly = Assembly.Load(ApiNamespace);

    [Fact]
    public void Api_has_no_services_namespace_to_confuse_with_application_services()
    {
        // Los servicios de negocio viven en Application/Services. Una carpeta Api/Services invita a poner ahí lógica
        // que no es de la Api; los adaptadores de HttpContext van en Api/RequestContext.
        var misplaced = ApiAssembly.GetTypes()
            .Where(type => type.ResidesIn(ApiNamespace + ".Services"))
            .Select(type => type.FullName);

        Assert.Empty(misplaced);
    }

    [Fact]
    public void HttpContext_adapters_live_in_the_request_context_namespace()
    {
        // Lo que lee la petición a través de IHttpContextAccessor (usuario actual, IP, user agent) es un adaptador de
        // HttpContext para Application: vive junto a los demás, no repartido por la Api.
        var adapters = ApiAssembly.GetTypes()
            .Where(type => type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(constructor => constructor.GetParameters()
                    .Any(parameter => parameter.ParameterType.FullName == HttpContextAccessorType)))
            .ToArray();

        // Si nadie usara IHttpContextAccessor, la regla de abajo pasaría en silencio.
        Assert.NotEmpty(adapters);

        var outside = adapters
            .Where(type => !type.ResidesIn(RequestContextNamespace))
            .Select(type => type.FullName);

        Assert.Empty(outside);
    }
}
