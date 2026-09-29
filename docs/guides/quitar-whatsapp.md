# Quitar WhatsApp

Cómo sacar el módulo WhatsApp de un proyecto que sale de la plantilla y no lo usa. WhatsApp es un [módulo opcional](../architecture/backend.md#módulos-opcionales) ([ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md)): vive en carpetas `Modules/WhatsApp`, se registra en un bloque de `Program.cs` y el núcleo no lo nombra (lo verifica `ModuleBoundaryTests`). Quitarlo es borrar esas carpetas, el bloque, su configuración y su documentación, y generar una migración que borra sus dos tablas. Después de eso, el build sale sin advertencias y los tests del núcleo pasan.

Todo se corre desde la raíz del repo, en un commit propio. Los comandos son de bash (en Windows, Git Bash); donde cambia, está también el de PowerShell.

## Antes de empezar

- **Las cuentas que solo tienen el número se quedan sin forma de entrar.** Sin el módulo nadie manda códigos por teléfono ni enlaces de ingreso. Si ya hay cuentas así (las que creó el bot, o un administrador con solo el número), buscalas y cargales un correo desde la administración antes de quitarlo: con el correo, entran con el código y queda verificado.

  ```sql
  SELECT u."Id", u."DisplayName", u."PhoneNumber"
  FROM "AspNetUsers" AS u
  WHERE NOT u."IsDeleted" AND u."Email" IS NULL
    AND NOT EXISTS (SELECT 1 FROM "AspNetUserLogins" AS l WHERE l."UserId" = u."Id");
  ```

- **Si la base ya está desplegada**, leé antes [Si la base ya está desplegada](#si-la-base-ya-está-desplegada): la migración borra dos tablas, y eso cambia el despliegue.
- **Si en la misma máquina tenés la plantilla u otro proyecto que salió de ella**, fijate que el tuyo tenga su propio `UserSecretsId` (en `src/ArquitecturaBase.Api/ArquitecturaBase.Api.csproj` y en `src/ArquitecturaBase.AppHost/ArquitecturaBase.AppHost.csproj`) y su propio volumen de Postgres (`arquitecturabase-pgdata`, en `src/ArquitecturaBase.AppHost/AppHost.cs`). Si comparten los de la plantilla, `dotnet user-secrets remove` le saca los secretos también a la otra copia, y `aspire run` le aplica la migración de esta guía a la base de la otra, que se queda sin las tablas de WhatsApp.

## Los pasos

1. **Borrá las carpetas del módulo**, enteras: una en cada proyecto de `src` y de `tests`, con sus `AGENTS.md`, sus tests y los textos del bot (`Bot.resx`).

   ```bash
   rm -rf src/ArquitecturaBase.Domain/Modules/WhatsApp \
          src/ArquitecturaBase.Application/Modules/WhatsApp \
          src/ArquitecturaBase.Infrastructure/Modules/WhatsApp \
          src/ArquitecturaBase.Api/Modules/WhatsApp \
          tests/ArquitecturaBase.Domain.UnitTests/Modules/WhatsApp \
          tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp \
          tests/ArquitecturaBase.ArchitectureTests/Modules/WhatsApp \
          tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp
   ```

   En PowerShell: `Remove-Item -Recurse -Force src/ArquitecturaBase.*/Modules/WhatsApp, tests/ArquitecturaBase.*/Modules/WhatsApp`. Revisá que no quedó ninguna: `ls src/*/Modules` no tiene que listar `WhatsApp` (ver [Trampas](#trampas)). Las carpetas `Modules` que quedan vacías se pueden borrar o dejar.

   Fuera de esas ocho carpetas no hay código del módulo. Los tests del núcleo que lo incluían (`ApiFactory`, `ExplicitRouteInventoryTests`, `DependencyInjectionTests`, `ApplicationHelpersTests`, `TransactionBoundaryTests`) declaran ganchos `partial void`: sin la parte del módulo, el compilador borra las llamadas.

2. **Sacá el módulo de `src/ArquitecturaBase.Api/Program.cs`:** los tres `using` marcados

   ```csharp
   using ArquitecturaBase.Api.Modules.WhatsApp;            // módulo WhatsApp
   using ArquitecturaBase.Application.Modules.WhatsApp;    // módulo WhatsApp
   using ArquitecturaBase.Infrastructure.Modules.WhatsApp; // módulo WhatsApp
   ```

   y el bloque del módulo, con su comentario:

   ```csharp
   // Módulo WhatsApp (ADR 0007). Para quitarlo: este bloque, los tres using marcados y docs/guides/quitar-whatsapp.md.
   // Apagado sin WhatsApp:PhoneNumberId, como Google sin su ClientId: la app arranca igual.
   builder.Services
       .AddWhatsAppApplication()
       .AddWhatsAppInfrastructure(builder.Configuration)
       .AddWhatsAppApi();
   ```

   `Program.cs` es el único archivo del núcleo que puede nombrar un módulo.

3. **Sacá la configuración.** En `src/ArquitecturaBase.Api/appsettings.json`, la sección `"WhatsApp"` entera y, en `"RateLimiting"`, `"WhatsAppWebhookPermitLimit"` y `"WhatsAppWebhookWindowMinutes"` (con la coma que queda colgando después de `"LoginVerifyWindowMinutes"`). En `src/ArquitecturaBase.Api/appsettings.Development.json`, la sección `"WhatsApp"`, que es la última: sacá también la coma después de la llave de `"Seed"`. El núcleo no lee ninguna de esas claves; una que quede no rompe nada, pero confunde. `Authentication:LoginLink` **no** es del módulo: se queda (ver [Lo que queda](#lo-que-queda-con-whatsapp-en-el-nombre-a-propósito)).

4. **Compilá:**

   ```bash
   dotnet build ArquitecturaBase.slnx
   ```

   Tiene que salir sin advertencias. Un `using` del módulo que quedó da `CS0246`, y uno que sobra, `IDE0005`, que el build trata como error.

5. **Generá la migración que borra las tablas**, antes de correr los tests y antes de levantar la app (ver [Trampas](#trampas)). Con `dotnet ef` instalado ([guía de la migración](migracion.md), paso 1):

   ```bash
   dotnet ef migrations add RemoveWhatsApp --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
   ```

   No necesita Postgres: la cadena solo tiene que existir. Revisá lo generado. `Up` tiene que traer exactamente `DropTable("WhatsAppMessages")` y después `DropTable("WhatsAppContacts")`: la única clave foránea va de mensajes a contactos, y `WhatsAppContacts.UserId` no apunta a `AspNetUsers`. Si aparece otra tabla, algo se borró de más. `Down` las vuelve a crear, con sus índices (el filtrado `IX_WhatsAppMessages_PendingInbound` incluido) y la clave foránea. Las migraciones que las crearon (`WhatsAppMessages` y `WhatsAppInboundProcessing`) se quedan: nombran las entidades con cadenas y compilan sin el módulo.

   En tu máquina, la Api aplica `RemoveWhatsApp` al arrancar (`aspire run`). En un ambiente desplegado la aplica el bundle del pipeline, antes de la imagen, como cualquier otra ([despliegue](despliegue.md#el-orden-bundle-después-imagen)); leé antes [Si la base ya está desplegada](#si-la-base-ya-está-desplegada).

6. **Sacá la documentación del módulo:** borrá `docs/features/whatsapp.md`, `docs/guides/whatsapp-en-local.md` y esta guía, y arreglá las líneas que los enlazan:

   ```bash
   grep -rnE "whatsapp\.md|whatsapp-en-local\.md|quitar-whatsapp\.md" --include=*.md --include=*.cs . | grep -v "^./docs/history/\|^./docs/plans/"
   ```

   Aparecen `AGENTS.md` (el primer párrafo, los logs y "Más documentación"), `README.md` (las guías, las áreas y el párrafo de WhatsApp), `docs/architecture/backend.md`, `docs/features/identidad.md` y `administracion.md`, el ADR 0007 y los `AGENTS.md` de carpeta del núcleo que nombran el módulo (`Api/Controllers`, `Application/Services/Auth`, `Application/Services/Users` y `Domain/Authentication`). En cada una, sacá la mención o reescribila sin el módulo. Sin enlace, `CLAUDE.md` también nombra las carpetas de WhatsApp entre las que tienen `AGENTS.md`. Las reglas del núcleo que valen sin WhatsApp (el enlace de un solo uso, el 9 argentino, el enmascarado de los números) ya están en `identidad.md` y `administracion.md`. Dejá dos documentos:
   - el [spec del ingreso con WhatsApp](../specs/2026-09-22-ingreso-whatsapp-design.md): sus secciones 6.1, 6.3, 6.4, 6.6 y 10 a 14 describen reglas del núcleo (cuentas, códigos, el enlace, invitaciones y límites), y el código las cita;
   - el [ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md): explica por qué existen los módulos opcionales y la frontera, que siguen en el núcleo. Un ADR no se borra; su enlace a `whatsapp.md` puede quedar roto.

   El resto de la documentación nombra WhatsApp como ejemplo de un módulo (`backend.md`, "Módulos opcionales") o de algo que queda en el núcleo: se puede dejar. `docs/history` y `docs/plans` son historia y no se tocan.

7. **Comprobá** ([Lo verifica](#lo-verifica)):

   ```bash
   # Nada nombra el módulo (lo que genera EF lo nombra con cadenas y compila igual):
   grep -rnE "ArquitecturaBase\.(Domain|Application|Infrastructure|Api)(\.[A-Za-z]+)*\.Modules\.WhatsApp" \
     src tests --include=*.cs --include=*.csproj --exclude=*.Designer.cs --exclude=*ModelSnapshot.cs   # vacío
   dotnet build ArquitecturaBase.slnx                                                               # sin advertencias
   dotnet ef migrations has-pending-model-changes --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
   dotnet test                                                                                      # con Docker
   ```

   `has-pending-model-changes` tiene que responder `No changes have been made to the model since the last migration.` Sin Docker corren el build y Domain, Application y Architecture (`dotnet test --project tests/<Proyecto>/<Proyecto>.csproj`), pero el trabajo no está terminado hasta correr la integración. Si querés verlo andando: `aspire run`, `https://localhost:5173/account/login-methods` tiene que responder `"whatsapp":false`, y al terminar, `aspire stop`.

## Si la base ya está desplegada

`RemoveWhatsApp` se aplica con el bundle, como cualquier otra migración, pero borra, y la regla del despliegue es que lo que se borra sale en otro despliegue ([despliegue](despliegue.md#el-orden-bundle-después-imagen)): entre el bundle y la imagen nueva, la versión anterior, con el módulo, sigue corriendo sin sus tablas. En ese rato responden 500 lo que las toca: cambiar o desvincular un número (el participante del número del módulo se registra aunque WhatsApp esté apagado), el detalle de un usuario invitado por WhatsApp (su estado de entrega) y, si el webhook estaba prendido, los avisos de Meta, que Meta reintenta. La retención de mensajes deja un error en el log. Apagar WhatsApp antes no alcanza. Si esa ventana no se puede aceptar, hacelo en dos despliegues:

1. **Primero**, la versión sin el módulo con `RemoveWhatsApp` sin los `DropTable`: vaciá su `Up` y su `Down` a mano, con un comentario que diga por qué (las tablas quedan en la base, sin uso). El snapshot ya no las tiene, así que `has-pending-model-changes` no da cambios.
2. **Después**, otra migración generada (`dotnet ef migrations add DropWhatsAppTables`, que sale vacía) con los dos `DropTable` escritos a mano en su `Up` y, en su `Down`, lo que había generado el `Down` de `RemoveWhatsApp`. Es una migración escrita a mano adentro de una generada vacía, la excepción que permite [la guía de la migración](migracion.md#trampas).

**Los datos:** `RemoveWhatsApp` borra los mensajes del chat y los contactos (qué chat era de qué cuenta). Si los querés conservar, exportalos antes, por ejemplo con `pg_dump -t '"WhatsAppMessages"' -t '"WhatsAppContacts"'`. Lo demás se queda: el número de cada cuenta, las invitaciones que salieron por WhatsApp (en el detalle, sin estado de entrega), las auditorías de ingreso y los enlaces y códigos que estaban en vuelo, que vencen solos a los 10 minutos.

**Si el proyecto ya estaba en producción con WhatsApp antes de las migraciones `UserInvitationProviderMessageId` y `LoginCodePhoneChannel`** de la plantilla, esas dos también van en dos despliegues, y después de desplegar se vuelve a correr el `UPDATE` de los códigos: está en [despliegue](despliegue.md#el-orden-bundle-después-imagen). Sin el módulo vale igual: la imagen nueva no lee un código guardado con `'WhatsApp'`.

**Fuera del repo:** en cada ambiente, sacá las variables `WhatsApp__*` (`WhatsApp__PhoneNumberId`, `WhatsApp__AccessToken`, `WhatsApp__AppSecret`, `WhatsApp__VerifyToken` y las que pisaban un valor por defecto) y `RateLimiting__WhatsAppWebhook*`, y sus secretos en el orquestador. En la app de Meta, sacá la URL del webhook (`…/webhooks/whatsapp`, que ya no existe) y revocá el token del usuario del sistema.

## Lo que ve la web

El front no se toca: todo depende de `GET /account/login-methods`, que responde `"whatsapp": false`, sin países y sin número.

- El ingreso ofrece solo el correo (y Google, si está configurado), y `/ingresar` no muestra "Volver a WhatsApp".
- El alta de un usuario pide el correo y no ofrece el número ni la invitación por WhatsApp. La Api igual acepta una cuenta con solo teléfono, de cualquier país, pero desde la web no se cargan números nuevos: sin canal, nadie les manda un código.
- La edición muestra el número solo si la cuenta ya tiene uno, y un administrador lo puede desvincular (`DELETE /api/users/{id}/whatsapp`, que es del núcleo).
- En `/perfil`, una cuenta con número lo sigue viendo, y el botón "Desvincular" sigue a la vista, pero responde 404: `/api/me/whatsapp` era del módulo. Lo desvincula un administrador. Si querés esconder el botón, está en `LoginMethodsCard.tsx` del front.
- El detalle de un usuario invitado por WhatsApp muestra "Invitación por WhatsApp" sin estado de entrega, y el reenvío ofrece solo el correo.

Lo que se puede limpiar en el front, si querés: el botón del perfil, los rótulos "WhatsApp" del teléfono y la marca "También entra con WhatsApp" de la lista, el código que ya no se usa (`WhatsAppCodeForm`, `UnlinkWhatsAppDialog`, el pedido del código por WhatsApp), la sección "Ingreso con WhatsApp" de su `CLAUDE.md` y `/webhooks` en `vite.config.ts` (ver abajo).

## Opcional

Nada de esto rompe el build ni los tests si lo dejás:

- **El túnel del AppHost.** `src/ArquitecturaBase.AppHost/AppHost.cs` trae un túnel de Dev Tunnels para que Meta llegue al webhook en local; viene apagado (`DevTunnel:Enabled`). Si no vas a recibir webhooks de otro proveedor en local, sacalo entero, porque las tres piezas van juntas: en `AppHost.cs`, el `using Aspire.Hosting.DevTunnels;` y el bloque que empieza con el comentario `// Túnel para que Meta le pegue al webhook de WhatsApp en local` y termina en la llave del `if`; `<PackageReference Include="Aspire.Hosting.DevTunnels" />` en `ArquitecturaBase.AppHost.csproj`, y su `<PackageVersion>` en `Directory.Packages.props`.
- **El paquete de resiliencia de Infrastructure.** En `src/ArquitecturaBase.Infrastructure/ArquitecturaBase.Infrastructure.csproj`, `<PackageReference Include="Microsoft.Extensions.Http.Resilience" />` con su comentario (`El cliente de WhatsApp reemplaza…`). **No** lo saques de `Directory.Packages.props`: lo usa `ServiceDefaults`.
- **La sección de `.editorconfig`** `[src/ArquitecturaBase.Infrastructure/Modules/WhatsApp/WhatsAppInfrastructureRegistration.cs]`, que apaga `EXTEXP0001` para un archivo que ya no existe.
- **`.codex/config.toml`**, que registra el servidor MCP de Meta (`whatsapp_business_tools`) para Codex.
- **Tus user-secrets locales** (si tu proyecto tiene sus propios `UserSecretsId`; ver [Antes de empezar](#antes-de-empezar)): `WhatsApp:AccessToken`, `WhatsApp:AppSecret` y `WhatsApp:VerifyToken` de la Api, y `DevTunnel:Enabled` y `DevTunnel:TunnelId` del AppHost, con `dotnet user-secrets remove "<clave>" --project src/ArquitecturaBase.Api` (o `--project src/ArquitecturaBase.AppHost`).
- **Dos textos del núcleo que dicen "WhatsApp"**: `Users.Identity.Required` en `Errors.resx` ("Cargá un correo o un número de WhatsApp.") y `LoginLinkTokenFormat` en `Validation.resx` ("…Abrilo de nuevo desde WhatsApp."), con sus `.en.resx`. Si los cambiás, cambiá también los tests que los afirman (`CreateUserWithPhoneTests` y `LoginLinkTests`).
- **`/webhooks`** queda en `BackendPrefixes` a propósito: es el prefijo de cualquier webhook de un proveedor, y así una ruta inexistente bajo él responde un ProblemDetails y no el `index.html`. Si igual lo querés sacar, son los cuatro lugares de la [guía del prefijo](prefijo-de-backend.md), al revés: `BackendPrefixes` en `SpaExtensions.cs` junto con el `[InlineData("/webhooks/no-existe")]` de `SpaHostingTests` (si sacás solo el primero, el test falla), el `route.Contains(" /webhooks/", …)` del filtro de `ExplicitRouteInventoryTests` y `/webhooks` en el `server.proxy` de `vite.config.ts`, en el repo del front; y la fila de `/webhooks` del `README.md`.

**Una sola migración inicial, solo sin ninguna base desplegada.** En lugar de `RemoveWhatsApp`, podés aplastar todas las migraciones en una inicial nueva: borrá el contenido de `src/ArquitecturaBase.Infrastructure/Persistence/Migrations/` (migraciones, `.Designer.cs` y el snapshot) y generá `Initial` con el comando del paso 5. Dos cosas más: borrá `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/StoredValuesMigrationTests.cs`, que prueba dos migraciones de la cadena vieja, y borrá tu base local (con `aspire stop`, el volumen de Postgres), que guarda el historial de las migraciones viejas. Con una base desplegada, no: el bundle intentaría crear tablas que ya existen.

## Lo que queda con "WhatsApp" en el nombre, a propósito

No lo toques: son valores guardados, contratos con el front o piezas del núcleo que valen sin el módulo.

- `UserInvitationChannel.WhatsApp` y `LoginMethod.WhatsAppCode` y `WhatsAppLink`: están guardados en la base (invitaciones y auditorías de ingreso) y viajan en el JSON.
- `DELETE /api/users/{id}/whatsapp` (`UsersController`): el número es un dato de la cuenta, y desvincularlo sigue sirviendo.
- `whatsapp`, `whatsappCountries` y `whatsappNumber` de `GET /account/login-methods`: sin el módulo responden `false`, `[]` y `null`, y así el front no ofrece nada de WhatsApp.
- Las claves `Auth.WhatsApp.CountryNotSupported`, `Users.Invitation.ConsentRequired` y `Users.Invitation.NameRequired` de `Errors.resx`, y `LoginCodeFormatWhatsApp`, `InvitationPhoneRequired` e `InvitationWhatsAppUnavailable` de `Validation.resx` (con sus `.en.resx`, sus accesores en `ValidationMessages` y los errores de `UserInvitationErrors`). Sin el módulo, una invitación con `channel: "WhatsApp"` se rechaza en el campo del canal con "WhatsApp no está disponible en este sistema."
- El enlace de ingreso (`LoginLink`, `/account/login-link/*` y `Authentication:LoginLink` en `appsettings.json`): es un enlace de un solo uso genérico ([identidad.md](../features/identidad.md)). Sin el módulo nadie lo emite, pero la capacidad queda.
- Los puertos del núcleo (`Application/Interfaces/Channels`, con `DisabledPhoneChannel` y `EmailInvitationChannel`), `ModuleBoundaryTests` y los ganchos de los tests: son el mecanismo de los módulos opcionales, que sirve para el próximo.
- Los comentarios que citan el spec del ingreso con WhatsApp o nombran el módulo como ejemplo ("con WhatsApp, el contacto del chat"): describen el puerto y no dependen del módulo.

## Trampas

- **Que no quede la carpeta.** Borrá cada `Modules/WhatsApp` entera, no solo sus archivos: una carpeta que queda en `src`, aunque esté vacía o tenga un archivo sin trackear, `ModuleBoundaryTests` la cuenta como un módulo, y `Program_composes_every_module_with_a_registration_per_layer` falla con `WhatsApp: Program.cs does not name the module`. Pasa al borrar desde el IDE con archivos bloqueados, o con un `git rm` que deja lo que no está en git.
- **No levantes la app antes de generar `RemoveWhatsApp`.** En Development la Api aplica las migraciones al arrancar, y EF lanza `PendingModelChangesWarning` si el modelo cambió y falta la migración: la Api no arranca. Por la misma razón, sin ella falla `MigrationsTests.Model_has_no_pending_changes`.
- **Un `using` que sobra rompe el build** (`IDE0005` con `TreatWarningsAsErrors`): si sacaste el bloque de `Program.cs` y dejaste un `using`, o al revés, no compila.
- **La migración se genera sobre el código ya borrado:** si la generás antes del paso 1, sale vacía y el snapshot sigue con las tablas.

## Lo verifica

- Sin Docker: el `grep` y el build del paso 7, `has-pending-model-changes` y `ModuleBoundaryTests` (`dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-class "ArquitecturaBase.ArchitectureTests.ModuleBoundaryTests"`): el núcleo no nombra un módulo y cada módulo que queda tiene su registro.
- Con Docker, los tests del núcleo que cambian de comportamiento sin el módulo: `MigrationsTests` (con `RemoveWhatsApp` en la cadena), `StoredValuesMigrationTests`, `ExplicitRouteInventoryTests` (37 rutas), `LoginMethodsControllerTests` (`whatsapp: false`), `UserInvitationEndpointsTests` (una invitación por WhatsApp se rechaza en el canal), `CreateUserWithPhoneTests` (una cuenta con solo teléfono, de cualquier país, se crea) y `UnlinkUserPhoneEndpointTests`.
