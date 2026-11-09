# Quitar ARCA

Para un derivado que no usa ARCA, o que no usa una de sus familias de servicios. No apagues opciones para simular la
retirada: quitá código, configuración y rutas, y generá la migración del modelo nuevo. La regla del área está en
[ARCA](../features/arca.md) y las familias, en el [ADR 0025](../decisions/0025-familias-de-servicios-arca.md).

Quitar el módulo borra sus siete tablas, con la clave privada, los tickets y la cola de emisión: respaldá la base y guardá
aparte el anillo `Arca:SecretProtection` si querés poder leer un backup viejo. Los comprobantes ya emitidos siguen en ARCA
y sus PDF fiscales, en el almacén privado (`arca-vouchers/`); sin el módulo, la limpieza de archivos deja de retenerlos. El certificado sigue existiendo en ARCA hasta que se lo
revoque allá. El núcleo no nombra el módulo: WhatsApp, formularios e IA siguen igual.

## El módulo entero

### Backend

1. Leé [módulos opcionales](../architecture/backend.md#módulos-opcionales), la [regla de ARCA](../features/arca.md) y
   [migración](migracion.md).
2. Borrá las diez carpetas del módulo: `Modules/Arca` en los cuatro proyectos de `src` y en los cuatro de `tests`, y
   `TestFeatures/Modules/Arca` en Application.UnitTests y Api.IntegrationTests. Antes de borrar, verificá que cada ruta
   esté dentro de tu proyecto. No toques los otros módulos.

   ```bash
   rm -rf src/ArquitecturaBase.Domain/Modules/Arca \
          src/ArquitecturaBase.Application/Modules/Arca \
          src/ArquitecturaBase.Infrastructure/Modules/Arca \
          src/ArquitecturaBase.Api/Modules/Arca \
          tests/ArquitecturaBase.Domain.UnitTests/Modules/Arca \
          tests/ArquitecturaBase.Application.UnitTests/Modules/Arca \
          tests/ArquitecturaBase.ArchitectureTests/Modules/Arca \
          tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca \
          tests/ArquitecturaBase.Application.UnitTests/TestFeatures/Modules/Arca \
          tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/Arca
   ```

3. En `src/ArquitecturaBase.Api/Program.cs`, quitá los tres `using` marcados con `// módulo ARCA` y el bloque del
   módulo, con su comentario:

   ```csharp
   // Módulo ARCA (ADR 0023). Para quitarlo: este bloque, los using marcados y docs/guides/quitar-arca.md.
   // Su anillo externo es obligatorio, como el de IA; el certificado de la empresa no.
   builder.Services
       .AddArcaApplication()
       .AddArcaInfrastructure(builder.Configuration)
       .AddArcaApi();
   ```

4. Quitá lo que nombra al módulo fuera de sus carpetas:
   - la sección `Arca` de `src/ArquitecturaBase.Api/appsettings.json`, que es la última: también la coma que cierra la
     sección anterior;
   - en el `.editorconfig` de la raíz, la sección
     `[src/ArquitecturaBase.Infrastructure/Modules/Arca/ArcaInfrastructureRegistration.cs]`, que suprime `EXTEXP0001`;
   - las 19 claves `Arca.*` de `Errors.resx` y `Errors.en.resx`. Si queda una,
     `ErrorCodeTests.Every_error_key_belongs_to_a_code_declared_in_domain` falla y la nombra;
   - la configuración externa `Arca:*` del host (user-secrets o variables `Arca__*`). Guardá una copia segura del anillo
     para backups viejos, fuera de Git.
5. El módulo no agregó paquetes. `System.Security.Cryptography.Pkcs` queda sin uso directo, pero **no se quita**: es el
   pin de seguridad de la dependencia transitiva de MailKit (MimeKit), y sin él la restauración falla con `NU1902` y
   `NU1903`.
6. **Conservá todas las migraciones anteriores**: nombran las entidades con cadenas y compilan sin el módulo. Generá
   `RemoveArca` sobre el código ya borrado, antes de levantar la app:

   ```bash
   dotnet restore ArquitecturaBase.slnx
   dotnet ef migrations add RemoveArca --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=build;Database=build;Username=build;Password=build" --ConnectionStrings:cache "localhost:6379"
   ```

   `Up` borra sólo las siete tablas del módulo: `ArcaAccessTickets`, `ArcaCertificates`, `ArcaInvoicingSettings`,
   `ArcaRegistryCompanies`, `ArcaConnections`, `ArcaVouchers` y `ArcaVoucherSequences`. Si aparece otra tabla, algo se borró de más. `Down` las recrea.

   Después compilá, revisá que el modelo no tenga cambios pendientes y corré todos los tests:

   ```bash
   dotnet build ArquitecturaBase.slnx
   dotnet ef migrations has-pending-model-changes --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api -- --environment Development --ConnectionStrings:appdb "Host=build;Database=build;Username=build;Password=build" --ConnectionStrings:cache "localhost:6379"
   dotnet test
   ```

   Aplicala con el bundle antes de la imagen nueva ([despliegue](despliegue.md#el-orden-bundle-después-imagen)), con
   las rutas de ARCA de la versión vieja detenidas. Los permisos `arca.*` quedan sin efecto, porque el catálogo ya no los
   tiene.

### Front

Quitá `src/features/arca` y lo que la compone afuera:

- En `src/app/routes.tsx`, la rama `ProtectedRoute permission="arca.read"` de `/configuracion/arca`.
- En `src/layouts/navigation.ts`, el ítem `navigation.arca`, y su texto en `common.json`, en español y en inglés.
- El namespace `arca`: `src/locales/es/arca.json` y `src/locales/en/arca.json`. Se cargan solos, no hay que registrar
  nada más.
- `src/test/visual/arca.spec.ts`, `src/test/visual/arcaFixtures.ts` y las 18 capturas
  `src/test/visual/screenshots/arca-*.png`. En
  `src/test/visual/fixtures.ts`, el `import` de `createArcaVisualFixtures`, la constante `arcaFixture`, su parte de los
  permisos y su línea en el manejador de pedidos.

Las piezas de `src/shared` que usa la pantalla son de la biblioteca común y se quedan, aunque alguna, como `Switch`,
quede sin otro uso.

```powershell
npm run build
npm run lint
npm run test
npm run test:visual
```

### Documentación

En el backend:

- Borrá `docs/features/arca.md`, `docs/guides/configurar-arca.md` y los tres planes de ARCA de `docs/plans`:
  `2026-10-31-arca-formularios-estandar.md` (la hoja de ruta), `2026-11-03-arca-etapa-1-conexion.md` y
  `2026-11-03-arca-etapa-1-implementacion.md`. De la hoja de ruta salen otros planes, como renglones y carpetas: ahí el
  enlace pasa a texto.
- Los ADR 0023 y 0025 quedan como decisiones registradas; sus enlaces a los planes borrados pasan a texto («plan
  retirado; ver Git»).
- Buscá lo que enlaza a lo borrado y arreglalo. Muchos planes enlazan a sus vecinos sin el prefijo `plans/`:

  ```bash
  git grep -lE "[(/]arca\.md|configurar-arca\.md|quitar-arca\.md|[0-9]{4}-[0-9]{2}-[0-9]{2}-arca-" -- '*.md' ':!docs/history' ':!docs/reviews'
  ```

- En `AGENTS.md`, ARCA en la lista de módulos opcionales, en las recetas y en `docs/features`; en
  [`backend.md`](../architecture/backend.md#módulos-opcionales), su guía de quitar; en el
  [índice de premisas](../architecture/premisas-de-la-plantilla.md), «y ARCA» de Features y la fila de ARCA; en el
  [mapa de instrucciones](../architecture/mapa-de-instrucciones.md), el bloque entero de ARCA: el párrafo, el encabezado
  de la tabla y sus diez filas.

En el front, `docs/design/arca-conexion.md`, la sección ARCA de `docs/design/visual-baseline.md`, la fila de
`src/features/arca` del [mapa](../../../ArquitecturaBaseFront/docs/mapa-de-instrucciones.md) y `arca/{connection,registry,invoicing,pages}`
del párrafo de guías por área, y el párrafo de `src/features/AGENTS.md`. El mismo `git grep`, sumando `arca-conexion\.md`.

Seguí la [auditoría documental](auditar-documentacion.md). Al terminar «Sin rastros», borrá también esta guía.

### Sin rastros

```bash
git grep -nE '\bArca|\barca\b|\bARCA\b' -- . ':!docs' ':!src/ArquitecturaBase.Infrastructure/Persistence/Migrations'
```

En el backend no devuelve nada: quedan sólo las migraciones, las anteriores y `RemoveArca`, que el filtro deja afuera. En el
front, corré `git grep -nE '\bArca|\barca\b|\bARCA\b' -- src ':!src/test/fixtures'`: las capturas HTTP de
`src/test/fixtures` pueden traer nombres generados por los tests, como `arca-3e26…`. Sin `-i`, para no encontrar «marca».

## Una familia sola

El ejemplo es `Invoicing` (facturación electrónica, `wsfe`). Otra familia se quita igual, con su nombre. `Connection` no
se quita sola: es la base de las demás; para quitarla, se quita el módulo entero. `Registry` trae la constancia de
inscripción, que es obligatoria: quitarla deja la conexión sin datos de la empresa.

### Backend

1. Borrá lo de la familia en cada capa: las carpetas `Invoicing` debajo de las carpetas de capa del módulo y los
   archivos de la raíz que la nombran (105 archivos el 2026-11-09, con la cola de emisión y el PDF fiscal).

   ```bash
   git ls-files -- 'src/**/Modules/Arca/**' 'tests/**/Modules/Arca/**' | grep -E '/Invoicing/|ArcaInvoicing[A-Za-z]*\.cs$' | xargs rm --
   ```

   Son `Contracts/Invoicing` y `Controllers/ArcaInvoicingController.cs` en Api; `Channels`, `Interfaces/Integrations`,
   `Interfaces/Persistence`, `Interfaces/Services`, `Models` y `Services`, cada una con su `Invoicing`, en Application;
   `ArcaInvoicingErrors.cs` e `Invoicing` en Domain; `Invoicing` y `Persistence/{Configurations,Readers,Repositories}/Invoicing`
   en Infrastructure, y la carpeta `Invoicing` de los tests de Domain, Application e integración.
2. En `ArcaApplicationRegistration` y `ArcaInfrastructureRegistration`, el método `AddInvoicing`, su llamada y sus
   `using`. En Infrastructure se va también `InvoicingHttpClientName`.
3. Las claves `Arca.Invoicing.*` de `Errors.resx` y `Errors.en.resx` (22 el 2026-11-09). QRCoder sólo lo usa el PDF fiscal:
   sacá su `PackageReference` de `ArquitecturaBase.Infrastructure.csproj` y su `PackageVersion` de `Directory.Packages.props`,
   y en el `.csproj`, el `EmbeddedResource` de `Modules\Arca\Invoicing\Pdf\Fonts`.
4. En `appsettings.json`, el servicio `wsfe` de los dos ambientes (`Arca:Testing:Services` y `Arca:Production:Services`).
   `ArcaCompositionTests` revisa las direcciones publicadas: sacale `wsfe`.
5. En los tests que comparten las familias:
   - en `TestFeatures/Modules/Arca` de Application.UnitTests, `FakeArcaInvoicingClient` y los registros de
     `IArcaInvoicingClient`, `IArcaInvoicingRepository` e `IArcaInvoicingReader` de `ArcaApplicationHost`, y lo de
     facturación de `FakeArcaStore`. `InvoicingFamily` se queda: es una definición de prueba genérica, sin código de la
     familia, y los tests de `Connection` la usan como el servicio que se puede elegir o no;
   - en `TestFeatures/Modules/Arca` de Api.IntegrationTests: `FakeArcaVouchers`, `ArcaVoucherScenario` y `ArcaMemoryFiles`;
     en `FakeArca`, `InvoicingService`, `FakeArcaPointOfSale`, `Vouchers` y sus operaciones, y las respuestas de `FEDummy` y
     `FEParamGetPtosVenta`; en `ArcaTestModule`, `Arca:Invoicing:Worker:ProcessInBackground`; en `ArcaHttpScenario.ActivateAsync`, el punto de venta; en
     `ArcaPersistenceScenario`, `Invoicing`, y quien lo use pasa a `Registry`;
   - en `ArcaTestModule`, las tres rutas de `api/arca/invoicing` y `InvoicingHttpClientName` de `ConfigureServices`;
   - en `ArcaConnectionEndpointsTests`, los tres `InlineData` de `api/arca/invoicing`, `wsfe` de los servicios instalados
     y el punto de venta del alta. Sin la familia, subir el certificado con la prueba en verde deja la conexión `Active`,
     y el caso sin facturación pasa a comprobar sólo eso;
   - en `ArcaFamilyBoundaryTests`, la familia de la lista; el test de la regla se reescribe con `Registry` de ejemplo y
     conserva los casos de la conexión que nombra a una familia (con una sola familia, «una familia nombra a otra» ya no
     se puede expresar);
   - cambian `wsfe` por `ws_sr_constancia_inscripcion` sólo los tests de integración que pasan por los servicios
     instalados. Ese servicio es obligatorio y ya tiene ticket después del alta: si un test necesita dos servicios, el
     segundo va como dato con otro código (`ArcaServiceCode.Create("otro_servicio")`), y una falla programada del WSAA ya
     no llega (el caso del servicio sin autorizar pasa a un ARCA que no responde: `Unavailable` y `NeedsAttention`). Los
     tests de Domain, de credenciales, del pedido de ticket y de opciones usan `wsfe` como dato y no cambian.
6. Generá `RemoveArcaInvoicing` con el mismo comando de arriba. `Up` borra sólo `ArcaInvoicingSettings`, `ArcaVouchers` y
   `ArcaVoucherSequences`, y `Down` las recrea. Con comprobantes emitidos, respaldá antes la base: la cola es la única copia
   local de lo que se emitió. Después: `dotnet build ArquitecturaBase.slnx`, `has-pending-model-changes` y `dotnet test`.

Sin la familia, la conexión se activa con la prueba en verde: no queda ninguna configuración por completar.

### Front

- Borrá `src/features/arca/invoicing`. En `pages/ArcaSettingsPage.tsx`, su línea de `familyZones` y su `import`.
- En el namespace `arca`, en español y en inglés, la sección `invoicing` y `services.names.wsfe`. Dos textos dan por
  sentada la facturación y conviene revisarlos: `certificate.soon` («para no cortar la facturación») y
  `disconnect.description` («Para volver a emitir…»).
- En `pages/ArcaSettingsPage.test.tsx`, `wsfe` de los datos de prueba, los casos y manejadores del punto de venta, y lo que
  nombra «Facturación electrónica»: la casilla, el texto de los pasos, el servicio sin autorizar (pasa al padrón), los
  interruptores y la cantidad de «Lo usa esta empresa». El conflicto se dispara con «Probar» respondiendo 409, porque el
  único interruptor que queda está fijo. El caso sin punto de venta queda como «sin una familia que configurar no
  aparece «Activar la conexión»».
- En `src/test/visual/arcaFixtures.ts`, `wsfe` y las respuestas de `api/arca/invoicing`. La escena `arca-unverified`
  («Sin verificar» con la prueba en verde) ya no puede pasar: dejala con una prueba en rojo (`lastTestPassed: false`), y
  en `arca.spec.ts` la parte de activación espera el título «4 · Prueba» en vez del botón y el punto de venta. Cambian las
  18 capturas: borrá `src/test/visual/screenshots/arca-*.png`, corré
  `npx playwright test src/test/visual/arca.spec.ts --update-snapshots=missing` y después sin el flag.

Corré `npm run build`, `npm run lint`, `npm run test` y `npm run test:visual`.

### Documentación

- En el backend: en [ARCA](../features/arca.md), la introducción, «Módulo y familias», las tres rutas de facturación, el
  límite remoto y la sección de la familia; en [configurar ARCA](configurar-arca.md), el punto de venta, `wsfe` y el
  límite remoto; los `AGENTS.md` del módulo que la nombran (Api, Domain e Infrastructure en `src`; Api.IntegrationTests,
  Application.UnitTests, Domain.UnitTests y los dos `TestFeatures/Modules/Arca` en `tests`) y sus filas del
  [mapa de instrucciones](../architecture/mapa-de-instrucciones.md). El [ADR 0025](../decisions/0025-familias-de-servicios-arca.md)
  no cambia.
- En el front: `src/features/arca/AGENTS.md`, `docs/design/arca-conexion.md`, la sección ARCA de
  `docs/design/visual-baseline.md`, `src/features/AGENTS.md` y `arca/{connection,registry,invoicing,pages}` de su mapa.

Para ver lo que quedó:

```bash
git grep -nE 'Invoicing|invoicing|wsfe|PointOfSale|[Pp]unto de venta' -- src tests
```

Quedan las migraciones, los comentarios de ejemplo de `ArcaServiceCode`, `ArcaOptions` e `IArcaServiceDefinition`, la
definición de prueba `InvoicingFamily` y los tests que usan `wsfe` como dato.

## Comprobaciones registradas

El 2026-11-05 dos agentes siguieron esta guía en copias descartables, un `git archive` de HEAD (backend `446e6b7f`, front
`62112ae4`), sin tocar los checkouts. La guía se corrigió con lo que encontraron: el paquete `Pkcs`, que no es del módulo,
la búsqueda de enlaces, los tests compartidos de la familia y lo que faltaba del front y de la documentación.

**El módulo entero:**

- Build sin advertencias; sin `System.Security.Cryptography.Pkcs` falla la restauración (`NU1902`/`NU1903`).
- `RemoveArca` con los cinco `DropTable` en `Up`; `Down` recrea las cinco tablas con sus índices.
- `has-pending-model-changes` sin cambios.
- Domain 559/559, Application 3698/3698 y arquitectura 197/197 (`ModuleBoundaryTests` y `ErrorCodeTests` incluidos).
- Integración con Docker: `Contracts*` 101/101, `OpenApiTests` 6/6 y `MigrationsTests` 3/3. La suite entera no se corrió.
- Front: build, lint (oxlint sin avisos y arquitectura 53/53) y vitest de app, layouts, locales, auth y test, 235/235. La
  suite visual no se corrió.
- Sin rastros, salvo las migraciones y una captura HTTP de `src/test/fixtures`.

**La familia `Invoicing`:**

- Build sin advertencias, `RemoveArcaInvoicing` con sólo `ArcaInvoicingSettings` y `has-pending-model-changes` sin
  cambios.
- Domain 618/618, Application 3729/3729 y arquitectura 200/200.
- Integración con Docker: `Modules.Arca*` 60/60, `Contracts*` 101/101, `OpenApiTests` 6/6 y `MigrationsTests` 3/3. La
  primera corrida dio 58/60, por cambiar `wsfe` en todos los tests de `Connection`; la regla quedó acotada arriba.
- Front: build, lint, vitest de arca, app, layouts y locales (128/128) y la escena visual de ARCA, 16/16 con las 18
  capturas regeneradas y sin la zona del punto de venta.
