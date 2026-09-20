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

builder.Build().Run();
