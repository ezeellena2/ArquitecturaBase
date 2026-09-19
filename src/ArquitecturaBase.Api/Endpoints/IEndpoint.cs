namespace ArquitecturaBase.Api.Endpoints;

/// <summary>Cada grupo de endpoints implementa esta interfaz y se registra solo (AddEndpoints + MapEndpoints).</summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
