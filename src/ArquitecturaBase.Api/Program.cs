using ArquitecturaBase.Api;
using ArquitecturaBase.Api.Hosting;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application;
using ArquitecturaBase.Infrastructure;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddTrustedForwardedHeaders(builder.Configuration);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddPresentation();

var app = builder.Build();

// Tiene que ser el primer middleware: el esquema y la IP pública alimentan la redirección HTTPS, el rate limiter,
// la autenticación y la auditoría. Sólo se aceptan encabezados de proxies declarados como confiables.
app.UseForwardedHeaders();

// Primero de todo: los encabezados se escriben cuando arranca la respuesta, así que los lleva cualquier
// respuesta, incluidos los errores que arman los middlewares de más adentro.
app.UseSecurityHeaders();

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
else
{
    // Fuera de desarrollo el certificado es real: el navegador puede recordar que este origen es solo HTTPS.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Después de la autenticación a propósito: el SPA es público, pero así sale dentro de UseStatusCodePages.
// El orden entre estos dos importa: un archivo que existe se sirve como archivo; el resto de las rutas del
// navegador abren el index.html.
app.UseStaticFiles();
app.UseSpaFallback();

app.MapDefaultEndpoints();
app.MapControllers();

await app.RunAsync();
