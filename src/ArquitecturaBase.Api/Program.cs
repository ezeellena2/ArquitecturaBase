using ArquitecturaBase.Api;
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application;
using ArquitecturaBase.Infrastructure;
using ArquitecturaBase.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPresentation();

var app = builder.Build();

// Primero la localización: todo lo que sigue, incluidos los errores, sale en el idioma pedido.
app.UseRequestLocalization();

// Dentro de la localización: el 500 también sale en el idioma pedido.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyMigrationsAsync();
    app.MapOpenApiDocumentation();
}

app.MapDefaultEndpoints();
app.MapEndpoints();

await app.RunAsync();
