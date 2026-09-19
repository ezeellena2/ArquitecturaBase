using ArquitecturaBase.Api;
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application;
using ArquitecturaBase.Infrastructure;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddPresentation();

var app = builder.Build();

// Primero la localización: todo lo que sigue, incluidos los errores, sale en el idioma pedido.
app.UseRequestLocalization();

// Dentro de la localización: el 500 también sale en el idioma pedido.
app.UseExceptionHandler();

// Las respuestas de error sin cuerpo (ruta inexistente, 405, 401/403 de la autorización) salen como ProblemDetails.
app.UseStatusCodePages();

// Como todo middleware que pueda cortar con un error, va después de UseStatusCodePages. El rechazo arma su propio
// ProblemDetails con retryAfter.
app.UseRateLimiter();

// Explícitos, y no los que WebApplication agrega solo al principio del pipeline: así quedan dentro de la
// localización y de UseStatusCodePages, y el 401/403 también sale como ProblemDetails traducido.
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyMigrationsAsync();
    await app.Services.SeedDatabaseAsync();
    app.MapOpenApiDocumentation();
}

app.MapDefaultEndpoints();
app.MapEndpoints();

await app.RunAsync();
