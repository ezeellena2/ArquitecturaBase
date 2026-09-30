# Quitar WhatsApp

Cómo sacar el módulo WhatsApp de un proyecto que sale de la plantilla y no lo usa. WhatsApp es un [módulo opcional](../architecture/backend.md#módulos-opcionales) ([ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md)): vive en carpetas `Modules/WhatsApp`, se registra en un bloque de `Program.cs` y el núcleo no lo nombra (lo verifica `ModuleBoundaryTests`). Quitarlo es borrar esas carpetas, el bloque, su configuración y su documentación, y generar una migración que borra sus dos tablas. Después de eso, el build sale sin advertencias y los tests del núcleo pasan.

Todo se corre desde la raíz del repo, en un commit propio, con Git Bash (en Windows también: los `grep` y las continuaciones con `\` no andan en PowerShell). Solo el paso 1 trae además su versión de PowerShell.

## Antes de empezar

- **Las cuentas que solo tienen el número se quedan sin forma de entrar.** Sin el módulo nadie manda códigos por teléfono ni enlaces de ingreso. Si ya hay cuentas así (las que creó el bot, o las que un administrador cargó con solo el número), buscalas y cargales un correo desde la administración antes de quitarlo: con el correo entran con el código, y el correo queda verificado. La consulta se corre en cada base: en local, con DBeaver, como dice el [README](../../README.md), o con `psql` adentro del contenedor (`docker ps --filter volume=<tu volumen>` da su nombre, y en Git Bash va `winpty docker exec -it <contenedor> psql -U postgres -d appdb`):

  ```sql
  SELECT u."Id", u."DisplayName", u."PhoneNumber"
  FROM "AspNetUsers" AS u
  WHERE NOT u."IsDeleted" AND u."Email" IS NULL
    AND NOT EXISTS (SELECT 1 FROM "AspNetUserLogins" AS l WHERE l."UserId" = u."Id");
  ```

- **Si la base ya está desplegada**, leé antes [Si la base ya está desplegada](#si-la-base-ya-está-desplegada): la migración borra dos tablas, y eso cambia el despliegue.
- **Si en la misma máquina tenés la plantilla u otro proyecto que salió de ella**, el tuyo tiene que tener su propio `UserSecretsId` (en `src/ArquitecturaBase.Api/ArquitecturaBase.Api.csproj` y en `src/ArquitecturaBase.AppHost/ArquitecturaBase.AppHost.csproj`), su propio volumen de Postgres (`.WithDataVolume("arquitecturabase-pgdata")` en `src/ArquitecturaBase.AppHost/AppHost.cs`) y, si los dos van a correr a la vez, su propio puerto (`port: 5433`, en el mismo archivo). Si son los de la plantilla, cambialos antes de seguir: un GUID nuevo en los dos `.csproj` (y volvé a cargar los secretos de la Api con `dotnet user-secrets set`: `Authentication:Google:ClientSecret` y `Email:Smtp:Password`, no los de WhatsApp ni `DevTunnel:*`), otro nombre de volumen (la base local arranca vacía, y el seed vuelve a crear al administrador de `Seed:AdminEmail`) y otro puerto (también en la tabla de DBeaver y en la línea de Postgres del `README.md`). Si no, `dotnet user-secrets remove` le saca los secretos también a la otra copia, y `aspire run` le aplica la migración de esta guía a la base de la otra, que se queda sin las tablas de WhatsApp. El contenedor de Postgres es persistente: `aspire stop` no lo apaga, y sigue ocupando el puerto.

## Los pasos

1. **Borrá las carpetas del módulo**, enteras: una en cada proyecto de `src` y de `tests`, con sus `AGENTS.md` y `CLAUDE.md`, sus tests y los textos del bot (`Bot.resx`).

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

   En PowerShell: `Remove-Item -Recurse -Force src/ArquitecturaBase.*/Modules/WhatsApp, tests/ArquitecturaBase.*/Modules/WhatsApp`. Revisá que no quedó ninguna (ver [Trampas](#trampas)): en Git Bash, `ls src/*/Modules tests/*/Modules` no tiene que listar `WhatsApp`; en PowerShell, `Get-Item src/*/Modules/WhatsApp, tests/*/Modules/WhatsApp` no tiene que mostrar nada. Las carpetas `Modules` que quedan vacías se pueden borrar o dejar.

   Fuera de esas ocho carpetas no hay código del módulo. Los tests del núcleo que lo incluían (`ApiFactory`, `ExplicitRouteInventoryTests`, `DependencyInjectionTests`, `ApplicationHelpersTests`, `TransactionBoundaryTests`) declaran ganchos `partial void`: sin la parte del módulo, el compilador borra las llamadas. Las demás clases `partial` del núcleo pierden sus tests del módulo y no hay que tocarlas.

2. **Sacá el módulo de `src/ArquitecturaBase.Api/Program.cs`:** los tres `using` marcados

   ```csharp
   using ArquitecturaBase.Api.Modules.WhatsApp;            // módulo WhatsApp
   using ArquitecturaBase.Application.Modules.WhatsApp;    // módulo WhatsApp
   using ArquitecturaBase.Infrastructure.Modules.WhatsApp; // módulo WhatsApp
   ```

   y el bloque del módulo, con su comentario y la línea en blanco que lo sigue:

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

   Tiene que salir sin advertencias. Con las carpetas ya borradas, un `using` del módulo que quedó da `CS0234` (el espacio de nombres `Modules` ya no existe), y una llamada del bloque que quedó, `CS1061`.

5. **Generá la migración que borra las tablas**, antes de correr los tests y antes de levantar la app (ver [Trampas](#trampas)). Con `dotnet ef` instalado ([guía de la migración](migracion.md), paso 1):

   ```bash
   dotnet ef migrations add RemoveWhatsApp --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=build;Database=build;Username=build;Password=build" --ConnectionStrings:cache "localhost:6379"
   ```

   No necesita Postgres: la cadena es de mentira, la misma que usa el pipeline para el bundle. `dotnet ef` corta el programa en `Build()`, antes de migrar, así que no se conecta; con una cadena de verdad tampoco se conectaría, pero con esta ni un comando equivocado llega a una base. EF avisa "An operation was scaffolded that may result in the loss of data": es esperado. Revisá lo generado. `Up` tiene que traer exactamente `DropTable("WhatsAppMessages")` y después `DropTable("WhatsAppContacts")`: la única clave foránea va de mensajes a contactos, y `WhatsAppContacts.UserId` no apunta a `AspNetUsers`. Si aparece otra tabla, algo se borró de más. `Down` las vuelve a crear, con sus índices (el filtrado `IX_WhatsAppMessages_PendingInbound` incluido) y la clave foránea. Las migraciones que las crearon (`WhatsAppMessages` y `WhatsAppInboundProcessing`) se quedan: nombran las entidades con cadenas y compilan sin el módulo.

   Si la generaste mal, borrá sus dos archivos y volvé el snapshot a como estaba (`git checkout -- src/ArquitecturaBase.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`). No uses `dotnet ef migrations remove`: ese sí consulta la base para saber si la migración se aplicó.

   En tu máquina, la Api aplica `RemoveWhatsApp` al arrancar (`aspire run`). En un ambiente desplegado la aplica el bundle del pipeline, antes de la imagen, como cualquier otra ([despliegue](despliegue.md#el-orden-bundle-después-imagen)); leé antes [Si la base ya está desplegada](#si-la-base-ya-está-desplegada).

6. **Sacá la documentación del módulo:** borrá `docs/features/whatsapp.md` y `docs/guides/whatsapp-en-local.md` (esta guía, al terminar el paso 7), y arreglá las líneas que los enlazan:

   ```bash
   grep -rnE "whatsapp\.md|whatsapp-en-local\.md|quitar-whatsapp\.md" --include=*.md --include=*.cs . | grep -v "^./docs/history/\|^./docs/plans/"
   ```

   Aparecen `AGENTS.md` (el primer párrafo, los logs y "Más documentación"), `README.md` (las guías, las áreas y el párrafo de WhatsApp), `docs/architecture/backend.md`, `docs/features/identidad.md` y `administracion.md` y los `AGENTS.md` de carpeta del núcleo que nombran el módulo (`Api/Controllers`, `Application/Services/Auth`, `Application/Services/Users` y `Domain/Authentication`). En cada una, sacá el enlace y la mención, o reescribila sin el módulo: las reglas del núcleo que valen sin WhatsApp (el enlace de un solo uso, el 9 argentino, el enmascarado de los números, el cambio de número) ya están en `identidad.md` y `administracion.md`. También aparece el ADR 0007, que no se toca (ver abajo).

   Sin enlace, estos también nombran el módulo, y hay que corregirlos:
   - `CLAUDE.md`, que nombra las carpetas de WhatsApp entre las que tienen `AGENTS.md`.
   - `docs/guides/despliegue.md`: en "Configuración obligatoria en Production", la fila de `WhatsApp:PhoneNumberId`, `WhatsApp:AccessToken`, `WhatsApp:AppSecret` y `WhatsApp:VerifyToken` entera; en la fila de `Authentication:Issuer`, "y los enlaces del bot" y la cita de `Modules/WhatsApp/WhatsAppInfrastructureRegistration.cs`; en el párrafo que sigue a la tabla, `WhatsApp:*`; y en "Colas en memoria", los mensajes de WhatsApp y `WhatsApp:QueueCapacity`. Lo de las migraciones `UserInvitationProviderMessageId` y `LoginCodePhoneChannel`, en "El orden: bundle, después imagen", se queda (salvo con la [migración inicial única](#opcional), que las borra).
   - `docs/deploy/azure-setup.md`: en "Pendientes antes de que esto sirva en producción", "y los enlaces del bot de WhatsApp" (y "salen" pasa a "sale"; lo mismo en la fila de `Authentication:Issuer` de `despliegue.md`).

   Revisalo con `grep -n "WhatsApp" docs/guides/despliegue.md docs/deploy/azure-setup.md`, que solo puede mostrar la sección de esas dos migraciones en `despliegue.md` (con la migración inicial única, nada). Dejá dos documentos:
   - el [spec del ingreso con WhatsApp](../specs/2026-09-22-ingreso-whatsapp-design.md): sus secciones 4, 5, 6.1, 6.3, 6.4, 6.6, 6.7 y 10 a 14 describen reglas del núcleo (cómo se prueba la identidad, el token en el fragmento, las cuentas, los códigos, el enlace, las invitaciones, la auditoría y los límites), y el código las cita;
   - el [ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md): explica por qué existen los módulos opcionales y la frontera, que siguen en el núcleo. Un ADR aceptado no se borra ni se reescribe ([decisiones](../decisions/README.md)): sus enlaces a `whatsapp.md` y a esta guía quedan rotos, y un revisor de enlaces los va a marcar.

   Después, revisá lo que queda con `grep -rn "WhatsApp\|whatsapp" AGENTS.md CLAUDE.md README.md docs/architecture docs/features docs/guides src --include=*.md`. Lo que dice "con WhatsApp, …" es un ejemplo de un módulo y se puede dejar, sacándole el enlace si lo tiene; lo mismo lo que nombra algo que queda en el núcleo (ver [Lo que queda](#lo-que-queda-con-whatsapp-en-el-nombre-a-propósito)). Lo que dice dónde está algo del módulo, o que algo lo hace hoy WhatsApp, se borra. `docs/history` y `docs/plans` son historia y no se tocan.

7. **Comprobá** ([Lo verifica](#lo-verifica)):

   ```bash
   # Nada nombra el módulo (lo que genera EF lo nombra con cadenas y compila igual):
   grep -rnE "ArquitecturaBase\.(Domain|Application|Infrastructure|Api)(\.[A-Za-z]+)*\.Modules\.WhatsApp" \
     src tests --include=*.cs --include=*.csproj --exclude=*.Designer.cs --exclude=*ModelSnapshot.cs   # vacío
   dotnet build ArquitecturaBase.slnx                                                               # sin advertencias
   dotnet ef migrations has-pending-model-changes --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api -- --environment Development --ConnectionStrings:appdb "Host=build;Database=build;Username=build;Password=build" --ConnectionStrings:cache "localhost:6379"
   dotnet test                                                                                      # con Docker
   ```

   `has-pending-model-changes` tiene que responder `No changes have been made to the model since the last migration.` Sin Docker corren el build y Domain, Application y Architecture (`dotnet test --project tests/<Proyecto>/<Proyecto>.csproj`), pero el trabajo no está terminado hasta correr la integración. Si querés verlo andando: `aspire run` (con tus propios secretos y volumen, ver [Antes de empezar](#antes-de-empezar)); `https://localhost:5173/account/login-methods` (o, sin el front al lado, `https://localhost:7180/account/login-methods`) tiene que responder `"whatsapp":false`; al terminar, `aspire stop`. Por último, borrá esta guía (`docs/guides/quitar-whatsapp.md`) y corré de nuevo el `grep` del paso 6: solo puede mostrar el ADR 0007, que no se toca.

## Si la base ya está desplegada

`RemoveWhatsApp` se aplica con el bundle, como cualquier otra migración, pero borra, y la regla del despliegue es que lo que se borra sale en otro despliegue ([despliegue](despliegue.md#el-orden-bundle-después-imagen)): entre el bundle y la imagen nueva, la versión anterior, con el módulo, sigue corriendo sin sus tablas. En ese rato responden 500 lo que las toca: cambiar o desvincular un número (el participante del número del módulo se registra aunque WhatsApp esté apagado), el detalle de un usuario invitado por WhatsApp (su estado de entrega) y, si el webhook estaba prendido, los avisos de Meta, que Meta reintenta. En el log quedan además errores que nadie ve: con WhatsApp prendido, cada código o invitación que sale llega igual pero no se guarda en el historial (`was sent but could not be saved in the history`); con el webhook prendido, el procesador de entrantes falla en cada vuelta (cada `WhatsApp:InboundPollSeconds`, 30 segundos), y la retención de mensajes falla si le toca correr. Apagar WhatsApp antes no alcanza. Si esa ventana no se puede aceptar, hacelo en dos despliegues ([la guía de la migración](migracion.md#trampas) lo permite como edición a mano):

1. **Primero**, la versión sin el módulo con `RemoveWhatsApp` sin los `DropTable`. Antes de tocarla, copiá aparte su `Up` y su `Down` tal como salieron: van en la migración del paso 2. Después vacialos a mano, con un comentario que diga por qué (las tablas quedan en la base, sin uso). El snapshot ya no las tiene, así que `has-pending-model-changes` no da cambios.
2. **Después**, otra migración generada (`dotnet ef migrations add DropWhatsAppTables`, que sale vacía) con el `Up` y el `Down` que copiaste, y un comentario que diga por qué están escritos a mano. Si no los guardaste, el `Down` sale del `Up` de `20260923174423_WhatsAppMessages` (las dos tablas, la clave foránea y sus índices) y del de `20260923214525_WhatsAppInboundProcessing` (el índice filtrado).

**Los datos:** `RemoveWhatsApp` borra los mensajes del chat y los contactos (qué chat era de qué cuenta). Si los querés conservar, exportalos antes, por ejemplo con `pg_dump -t '"WhatsAppMessages"' -t '"WhatsAppContacts"'`. Lo demás se queda: el número de cada cuenta, las invitaciones por WhatsApp (en el detalle, las que salieron quedan sin estado de entrega y las que no se pudieron mandar siguen diciendo "No llegó"), las auditorías de ingreso y los enlaces y códigos que estaban en vuelo, que vencen solos a los 10 minutos.

**Si el proyecto ya estaba en producción con WhatsApp antes de las migraciones `UserInvitationProviderMessageId` y `LoginCodePhoneChannel`** de la plantilla, no las apliques tal cual: reemplazalas por las dos migraciones en dos despliegues de [despliegue](despliegue.md#el-orden-bundle-después-imagen). La segunda vuelve a copiar `WaMessageId` y pasa a `'Phone'` los códigos que quedaron con `'WhatsApp'` antes de borrar la columna, así que no hace falta el `UPDATE` a mano, que es solo para quien aplica las de la plantilla en un único despliegue. Sin el módulo vale igual: la imagen anterior, con el módulo, sigue escribiendo `'WhatsApp'` y `WaMessageId` en esa ventana, y la imagen nueva no lee un código guardado con `'WhatsApp'`.

**Fuera del repo:** en cada ambiente, sacá las variables `WhatsApp__*` (`WhatsApp__PhoneNumberId`, `WhatsApp__AccessToken`, `WhatsApp__AppSecret`, `WhatsApp__VerifyToken` y las que pisaban un valor por defecto) y `RateLimiting__WhatsAppWebhook*`, y sus secretos en el orquestador. En la app de Meta, sacá la URL del webhook (`…/webhooks/whatsapp`, que ya no existe) y revocá el token del usuario del sistema.

## Lo que ve la web

El front no se toca: todo depende de `GET /account/login-methods`, que responde `"whatsapp": false`, sin países y sin número.

- El ingreso ofrece solo el correo (y Google, si está configurado), y `/ingresar` no muestra "Volver a WhatsApp".
- El alta de un usuario pide el correo y no ofrece el número ni la invitación por WhatsApp. La Api igual acepta una cuenta con solo teléfono, de cualquier país, pero desde la web no se cargan números nuevos: sin canal, nadie les manda un código.
- La edición muestra el número solo si la cuenta ya tiene uno, y un administrador lo puede desvincular (`DELETE /api/users/{id}/whatsapp`, que es del núcleo).
- En `/perfil`, una cuenta con número lo sigue viendo y lo puede desvincular (`DELETE /api/me/whatsapp`, del núcleo), sin cerrar sus sesiones. Si es su único medio de ingreso, responde `409 Users.User.LastLoginMethod`: primero tiene que agregar un correo verificado o vincular Google.
- El detalle de un usuario invitado por WhatsApp muestra "Invitación por WhatsApp": sin estado de entrega si salió, o "No llegó" si no se pudo mandar (ese fallo lo guarda el núcleo). El reenvío ofrece solo el correo.

Lo que se puede limpiar en el front, si querés: los rótulos "WhatsApp" del teléfono y la marca "También entra con WhatsApp" de la lista, el código que ya no se usa (`WhatsAppCodeForm`, el pedido del código por WhatsApp) y la sección "Ingreso con WhatsApp" de su `CLAUDE.md`. `/webhooks` en `vite.config.ts` se queda: es un prefijo del núcleo, y sacarlo solo de Vite deja una ruta inexistente bajo él respondiendo el `index.html` en desarrollo (ver [Opcional](#opcional)).

## Opcional

Nada de esto rompe el build ni los tests si lo dejás:

- **El túnel del AppHost.** `src/ArquitecturaBase.AppHost/AppHost.cs` trae un túnel de Dev Tunnels para que Meta llegue al webhook en local; viene apagado (`DevTunnel:Enabled`). Si no vas a recibir webhooks de otro proveedor en local, sacalo entero, porque las tres piezas van juntas: en `AppHost.cs`, el `using Aspire.Hosting.DevTunnels;` y el bloque que empieza con el comentario `// Túnel para que Meta le pegue al webhook de WhatsApp en local` y termina en la llave del `if`; `<PackageReference Include="Aspire.Hosting.DevTunnels" />` en `ArquitecturaBase.AppHost.csproj`, y su `<PackageVersion>` en `Directory.Packages.props`.
- **El paquete de resiliencia de Infrastructure.** En `src/ArquitecturaBase.Infrastructure/ArquitecturaBase.Infrastructure.csproj`, `<PackageReference Include="Microsoft.Extensions.Http.Resilience" />` con su comentario (`El cliente de WhatsApp reemplaza…`). **No** lo saques de `Directory.Packages.props`: lo usa `ServiceDefaults`.
- **La sección de `.editorconfig`** `[src/ArquitecturaBase.Infrastructure/Modules/WhatsApp/WhatsAppInfrastructureRegistration.cs]`, que apaga `EXTEXP0001` para un archivo que ya no existe.
- **`.codex/config.toml`**: registra solo el servidor MCP de Meta (`whatsapp_business_tools`) para Codex. Borrá el archivo, o solo `[mcp_servers.whatsapp_business_tools]` si le agregaste otros.
- **Tus user-secrets locales** (si tu proyecto tiene sus propios `UserSecretsId`; ver [Antes de empezar](#antes-de-empezar)): `WhatsApp:AccessToken`, `WhatsApp:AppSecret` y `WhatsApp:VerifyToken` de la Api, y `DevTunnel:Enabled` y `DevTunnel:TunnelId` del AppHost, con `dotnet user-secrets remove "<clave>" --project src/ArquitecturaBase.Api` (o `--project src/ArquitecturaBase.AppHost`).
- **Dos textos del núcleo que dicen "WhatsApp"**: `Users.Identity.Required` en `Errors.resx` ("Cargá un correo o un número de WhatsApp.", por ejemplo "Cargá un correo o un número de celular.") y `LoginLinkTokenFormat` en `Validation.resx` ("…Abrilo de nuevo desde WhatsApp.", por ejemplo "…Abrilo de nuevo desde el mensaje que te llegó."), con sus `.en.resx`. Si los cambiás, cambiá también los tests que afirman el texto en español: `CreateUserWithPhoneTests.Without_an_email_or_a_phone_the_request_is_rejected` y `Auth/LoginLinkTests.Malformed_or_missing_token_is_a_validation_error` (dos casos), los dos en `Api.IntegrationTests`.
- **`/webhooks`** queda en `BackendPrefixes` a propósito: es un prefijo del núcleo, el de los webhooks de cualquier proveedor ([backend.md, "Front y hosting del SPA"](../architecture/backend.md#front-y-hosting-del-spa)). Si igual lo querés sacar, son los cuatro lugares de la [guía del prefijo](prefijo-de-backend.md), al revés: `BackendPrefixes` en `SpaExtensions.cs` junto con el `[InlineData("/webhooks/no-existe")]` de `SpaHostingTests` (si sacás solo el primero, el test falla; sin el caso, la integración tiene un test menos), el `route.Contains(" /webhooks/", …)` del filtro de `ExplicitRouteInventoryTests` y `/webhooks` en el `server.proxy` de `vite.config.ts`, en el repo del front. Sacalo también de la fila "Quién atiende…" del `README.md` y de "Un solo origen" de `backend.md`, y en `prefijo-de-backend.md` borrá la trampa "`/webhooks` es del núcleo" y sacalo de la lista de la trampa de `SpaHostingTests`.

**Una sola migración inicial, solo sin ninguna base desplegada.** Reemplaza el paso 5; los pasos 6 y 7 siguen valiendo, salvo lo de las dos migraciones en `despliegue.md`, que con esta variante se borra (abajo). En lugar de `RemoveWhatsApp`, aplastá todas las migraciones en una inicial nueva:

```bash
rm src/ArquitecturaBase.Infrastructure/Persistence/Migrations/*.cs
rm tests/ArquitecturaBase.Api.IntegrationTests/Persistence/StoredValuesMigrationTests.cs
dotnet ef migrations add Initial --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=build;Database=build;Username=build;Password=build" --ConnectionStrings:cache "localhost:6379"
```

`StoredValuesMigrationTests` prueba dos migraciones de la cadena vieja, que ya no existen. Tu base local guarda el historial de las migraciones viejas, así que hay que borrarla, **solo si el volumen es de tu proyecto** (ver [Antes de empezar](#antes-de-empezar); si es el de la plantilla, le borrás su base): `aspire stop`, y después `docker ps -a --filter volume=<tu volumen>` para ver el contenedor, `docker rm -f <ese contenedor>` y `docker volume rm <tu volumen>`. Quedan documentos que citan lo que borraste, y se corrigen así: en `backend.md`, "Migraciones" usa `LoginCodePhoneChannel`, `UserInvitationProviderMessageId` y `StoredValuesMigrationTests` como ejemplo de una migración de datos y de un `RenameColumn` (dejá la regla sin esos nombres); en `despliegue.md`, "El orden: bundle, después imagen" habla de esas dos migraciones (borrá desde "Dos migraciones de la plantilla…" hasta el segundo bloque SQL); y el comentario de `LoginCodeChannel` (`src/ArquitecturaBase.Domain/Authentication/LoginCodeChannel.cs`) nombra `LoginCodePhoneChannel` como ejemplo (sacalo). Con una base desplegada, no: el bundle intentaría crear tablas que ya existen.

## Lo que queda con "WhatsApp" en el nombre, a propósito

No lo toques: son valores guardados, contratos con el front o piezas del núcleo que valen sin el módulo.

- `UserInvitationChannel.WhatsApp`: está guardado en la base (el canal de cada invitación) y viaja en el JSON (el `channel` de la última invitación de un usuario y el de los pedidos de invitación).
- `LoginMethod.WhatsAppCode` y `WhatsAppLink`: están guardados en la base (el método de las auditorías de ingreso), y el núcleo los sigue usando: `LoginCodeVerifier` audita con `WhatsAppCode` el ingreso con un código a un número, y `LoginLinkVerifier`, con `WhatsAppLink` el del enlace.
- `DELETE /api/users/{id}/whatsapp` (`UsersController`) y `DELETE /api/me/whatsapp` (`MeController`): el número es un dato de la cuenta, y desvincularlo sigue sirviendo. Desde el perfil conserva las sesiones y exige otro medio de ingreso.
- `whatsapp`, `whatsappCountries` y `whatsappNumber` de `GET /account/login-methods`: sin el módulo responden `false`, `[]` y `null`, y así el front no ofrece nada de WhatsApp.
- Las claves `Auth.WhatsApp.CountryNotSupported`, `Users.Invitation.ConsentRequired` y `Users.Invitation.NameRequired` de `Errors.resx`, y `LoginCodeFormatWhatsApp`, `InvitationPhoneRequired` e `InvitationWhatsAppUnavailable` de `Validation.resx` (con sus `.en.resx`, sus accesores en `ValidationMessages` y los errores de `UserInvitationErrors`). Sin el módulo, una invitación con `channel: "WhatsApp"` se rechaza en el campo del canal con "WhatsApp no está disponible en este sistema."
- El enlace de ingreso (`LoginLink`, `/account/login-link/*` y `Authentication:LoginLink` en `appsettings.json`): es un enlace de un solo uso genérico ([identidad.md](../features/identidad.md)). Sin el módulo nadie lo emite, pero la capacidad queda.
- `/webhooks` en los prefijos de backend, por lo que dice [Opcional](#opcional).
- Los puertos del núcleo (sus interfaces en `Application/Interfaces/Channels` y sus versiones del núcleo en `Application/Channels`: `DisabledPhoneChannel` y `EmailInvitationChannel`), `ModuleBoundaryTests` y los ganchos de los tests: son el mecanismo de los módulos opcionales, que sirve para el próximo.
- Los comentarios que citan el spec del ingreso con WhatsApp o nombran el módulo como ejemplo ("con WhatsApp, el contacto del chat"): describen el puerto y no dependen del módulo.

## Trampas

- **Que no quede la carpeta.** Borrá cada `Modules/WhatsApp` entera, no solo sus archivos: una carpeta que queda en `src`, aunque esté vacía o tenga un archivo sin trackear, `ModuleBoundaryTests` la cuenta como un módulo, y `Program_composes_every_module_with_a_registration_per_layer` falla con `WhatsApp: Program.cs does not name the module`. Pasa al borrar desde el IDE con archivos bloqueados, o con un `git rm` que deja lo que no está en git.
- **No levantes la app antes de generar `RemoveWhatsApp`.** En Development la Api aplica las migraciones al arrancar, y EF lanza `PendingModelChangesWarning` si el modelo cambió y falta la migración: la Api no arranca. Por la misma razón, sin ella fallan con `PendingModelChangesWarning` `MigrationsTests` y los tests que migran una base nueva o arrancan en Development o Production (`OpenApiTests`, `DatabaseSeederTests`, `SystemSettingsSeedTests`, `LastAdminLockTests`, `StoredValuesMigrationTests` y `ProductionStartupTests`): 17 casos.
- **`Program.cs` a medias no compila.** Con las carpetas ya borradas, un `using` del módulo que quedó da `CS0234`, y una llamada del bloque que quedó, `CS1061`. Si editás `Program.cs` antes de borrar las carpetas, un `using` que sobra da `IDE0005`, que con `TreatWarningsAsErrors` también rompe el build.
- **La migración se genera sobre el código ya borrado:** si la generás antes del paso 1, sale vacía y el snapshot sigue con las tablas.
- **La máquina compartida** (ver [Antes de empezar](#antes-de-empezar)): con los `UserSecretsId`, el volumen o el puerto de la plantilla, lo que hacés en tu proyecto le pasa también a ella.

## Lo verifica

- Sin Docker: el `grep` y el build del paso 7, `has-pending-model-changes` y `ModuleBoundaryTests` (`dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-class "ArquitecturaBase.ArchitectureTests.ModuleBoundaryTests"`): el núcleo no nombra un módulo y cada módulo que queda tiene su registro.
- Con Docker, los tests del núcleo que cambian de comportamiento sin el módulo: `MigrationsTests` (con `RemoveWhatsApp` en la cadena), `StoredValuesMigrationTests` (que migra hacia atrás y hacia adelante, así que prueba también el `Down` de `RemoveWhatsApp`; con la migración inicial única no existe), `ExplicitRouteInventoryTests` (38 rutas), `LoginMethodsControllerTests` (`whatsapp: false`), `UserInvitationEndpointsTests` (una invitación por WhatsApp se rechaza en el canal), `CreateUserWithPhoneTests` (una cuenta con solo teléfono, de cualquier país, se crea), `UnlinkUserPhoneEndpointTests` y `MePhoneEndpointsTests` (incluido `DELETE /api/me/whatsapp`, las guardas y las sesiones que siguen vigentes).
- La plantilla lo probó con la prueba de fuego de la Etapa 6: una copia descartable que siguió esta guía, con el build sin advertencias, `RemoveWhatsApp` con solo los dos `DropTable`, `has-pending-model-changes` sin cambios y los tests del núcleo en verde; también lo opcional, la migración inicial única y los dos despliegues de una base desplegada.
