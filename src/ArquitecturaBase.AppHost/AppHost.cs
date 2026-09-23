using Aspire.Hosting.DevTunnels;

var builder = DistributedApplication.CreateBuilder(args);

// Contraseña fija (Parameters:postgres-password). La usa también DBeaver.
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

// Puerto fijo 5433 (el 5432 lo usa el PostgreSQL local), contenedor persistente y volumen con nombre.
var postgres = builder.AddPostgres("postgres", password: postgresPassword, port: 5433)
    .WithDataVolume("arquitecturabase-pgdata")
    .WithLifetime(ContainerLifetime.Persistent);

var appDb = postgres.AddDatabase("appdb");

var api = builder.AddProject<Projects.ArquitecturaBase_Api>("api")
    .WithReference(appDb)
    .WaitFor(appDb)
    .WithUrlForEndpoint("https", _ => new() { Url = "/swagger", DisplayText = "Swagger UI" });

// El SPA vive en otro repo. El navegador habla solo con este origen y Vite reenvía a la Api (sección 5.1).
builder.AddViteApp("front", "../../../ArquitecturaBaseFront")
    .WithNpm()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpsEndpoint(port: 5173, env: "PORT", isProxied: false)
    .WithHttpsDeveloperCertificate();

// Túnel para que Meta le pegue al webhook de WhatsApp en local (spec de WhatsApp, sección 16). Viene apagado, así
// aspire run no le pide la CLI devtunnel a quien no la usa; se prende con DevTunnel:Enabled en los user-secrets del
// AppHost. Mientras está prendido, la Api queda en internet: se prende para probar y se apaga al terminar (aspire stop).
if (bool.TryParse(builder.Configuration["DevTunnel:Enabled"], out var devTunnelEnabled) && devTunnelEnabled)
{
    // La región fija hace que la URL sea siempre la misma, así Meta se configura una sola vez: sin región, el túnel se
    // puede crear de nuevo en otra, según el ping, y la URL cambia. El id no va fijo en el código porque es parte de
    // la dirección pública y es único entre todos los usuarios de Dev Tunnels: uno escrito acá chocaría con otra copia
    // de la plantilla y sería fácil de adivinar. Sin DevTunnel:TunnelId, la integración usa uno propio por máquina
    // (sale de la ruta del AppHost), que tampoco cambia entre arranques.
    var devTunnelOptions = new DevTunnelOptions { Region = DevTunnelRegion.BrazilSouth };
    var devTunnelId = builder.Configuration["DevTunnel:TunnelId"];

    // Solo el endpoint https de la Api: ni el http, ni el front, ni Postgres. El acceso anónimo va en ese puerto y no
    // en todo el túnel, porque Meta no inicia sesión.
    builder
        .AddDevTunnel(
            "tunnel",
            tunnelId: string.IsNullOrWhiteSpace(devTunnelId) ? null : devTunnelId,
            options: devTunnelOptions)
        .WithReference(api.GetEndpoint("https"), allowAnonymous: true);
}

builder.Build().Run();
