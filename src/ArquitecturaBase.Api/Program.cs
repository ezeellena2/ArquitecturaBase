using ArquitecturaBase.Api;
using ArquitecturaBase.Api.Hosting;
using ArquitecturaBase.Api.Modules.WhatsApp;            // módulo WhatsApp
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application;
using ArquitecturaBase.Application.Modules.WhatsApp;    // módulo WhatsApp
using ArquitecturaBase.Infrastructure;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp; // módulo WhatsApp
using ArquitecturaBase.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisCaching();

builder.Services.AddTrustedForwardedHeaders(builder.Configuration);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddPresentation();

// Módulo WhatsApp (ADR 0007). Para quitarlo: este bloque, los tres using marcados y docs/guides/quitar-whatsapp.md.
// Apagado sin WhatsApp:PhoneNumberId, como Google sin su ClientId: la app arranca igual.
builder.Services
    .AddWhatsAppApplication()
    .AddWhatsAppInfrastructure(builder.Configuration)
    .AddWhatsAppApi();

var app = builder.Build();

// Tiene que ser el primer middleware: el esquema y la IP pública alimentan la redirección HTTPS, el rate limiter,
// la autenticación y la auditoría. Sólo se aceptan encabezados de proxies declarados como confiables.
app.UseForwardedHeaders();

// Primero de todo: los encabezados se escriben cuando arranca la respuesta, así que los lleva cualquier
// respuesta, incluidos los errores que arman los middlewares de más adentro.
app.UseSecurityHeaders();

// También cubre la lectura de configuración que hace el proveedor de idioma.
app.UseExceptionHandler();

// Localiza los errores y usa la configuración general si no hay un idioma admitido en la petición.
app.UseRequestLocalization();

// Las respuestas de error sin cuerpo (ruta inexistente, 405, 401/403 de la autorización) salen como ProblemDetails.
app.UseStatusCodePages();

// Como todo middleware que pueda cortar con un error, va después de UseStatusCodePages. El rechazo arma su propio
// ProblemDetails con retryAfter.
app.UseRateLimiter();

// Explícitos, y no los que WebApplication agrega solo al principio del pipeline: así quedan dentro de la
// localización y de UseStatusCodePages, y el 401/403 también sale como ProblemDetails traducido.
app.UseAuthentication();
app.UseAuthorization();

// La base según el ambiente: salvo en Testing, primero valida las opciones (como ValidateOnStart, pero antes de tocar la
// base); Development migra y siembra; fuera de Development y Testing, no arranca con migraciones pendientes (las aplica
// el bundle) y siembra; Testing no hace nada, porque el arnés siembra después de crear el esquema.
await app.Services.InitializeDatabaseAsync(app.Environment);

if (app.Environment.IsDevelopment())
{
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
