# Quitar el módulo de formularios

Para un derivado que no usa formularios (`BusinessForms`). No apagues opciones para simular la retirada: quitá
código, configuración y rutas, y generá la migración del modelo nuevo. Esa migración borra las 26 tablas del módulo
(definiciones, borradores, carpetas, versiones, catálogos, respuestas y sus resultados, reglas, diseños de PDF, sugerencias y
datos de personas):
respaldá la base antes.

WhatsApp e IA siguen andando. Sin el módulo, el núcleo usa sus versiones apagadas de los puertos de formularios
(`DisabledStructuredFormSource`, `UnavailableStructuredRecordSink` y las demás de `Application/Channels`): WhatsApp no
encuentra formularios para mandar y el asistente pierde las capacidades de formularios. Con
`UnavailableStructuredAttachmentReader` nadie puede adjuntar una planilla: Inicio no muestra el clip (el bootstrap da
`attachments` en false) y una planilla que llega por WhatsApp queda No disponible, sin bajarse. Las tablas
`WhatsAppFlow*` son del otro módulo y se quedan.

## Backend

1. Leé [módulos opcionales](../architecture/backend.md#módulos-opcionales), la
   [regla de formularios](../features/formularios.md) y [migración](migracion.md).
2. Borrá las diez carpetas del módulo: `Modules/BusinessForms` en los cuatro proyectos de `src` y en los cuatro de
   `tests`, y `TestFeatures/Modules/BusinessForms` en Application.UnitTests y Api.IntegrationTests. Antes de borrar,
   verificá que cada ruta esté dentro de tu proyecto. No toques los otros módulos.

   ```bash
   rm -rf src/ArquitecturaBase.Domain/Modules/BusinessForms \
          src/ArquitecturaBase.Application/Modules/BusinessForms \
          src/ArquitecturaBase.Infrastructure/Modules/BusinessForms \
          src/ArquitecturaBase.Api/Modules/BusinessForms \
          tests/ArquitecturaBase.Domain.UnitTests/Modules/BusinessForms \
          tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms \
          tests/ArquitecturaBase.ArchitectureTests/Modules/BusinessForms \
          tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms \
          tests/ArquitecturaBase.Application.UnitTests/TestFeatures/Modules/BusinessForms \
          tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/BusinessForms
   ```

   Con ellas se van los controles de migraciones del módulo (`FormMigrationsTests`) y su publicador de prueba, que es
   la señal de las composiciones de WhatsApp: sin él, esas composiciones se omiten y el inventario de WhatsApp deja
   afuera sus siete rutas de formularios. No hace falta tocar otros tests.

3. En `src/ArquitecturaBase.Api/Program.cs`, quitá los tres `using` de `Modules.BusinessForms` y el bloque del módulo,
   con su comentario:

   ```csharp
   // Optional business forms module: draft authoring and recovery.
   builder.Services.AddBusinessFormsApplication().AddBusinessFormsInfrastructure().AddBusinessFormsApi();
   ```

4. Quitá lo que nombra al módulo fuera de sus carpetas. El módulo no tiene configuración en `appsettings`.
   - En `src/ArquitecturaBase.Infrastructure/ArquitecturaBase.Infrastructure.csproj`, el `ItemGroup` de las fuentes del
     PDF (`Modules\BusinessForms\Pdf\Fonts\*.ttf`).
   - En `src/ArquitecturaBase.Application/ArquitecturaBase.Application.csproj`, el `ItemGroup` de los estándares de la
     biblioteca (`Modules\BusinessForms\Resources\Standards\*.json`).
   - En `.editorconfig`, las dos secciones de `src/ArquitecturaBase.Domain/Modules/BusinessForms/…`.
   - Las 201 claves `Forms.*` de `Errors.resx` y `Errors.en.resx`. Si queda una,
     `ErrorCodeTests.Every_error_key_belongs_to_a_code_declared_in_domain` falla y la nombra.
   - `FormCatalogStateInvalid` de `ValidationMessages` y de `Validation.resx`/`.en.resx`.
   - `harnesses/assistant-qualification`, el arnés manual de calificación del asistente. Está fuera de la solución y el
     build no lo ve, pero usa el módulo y deja de compilar. Borralo, o dejale sólo `home-existing`, el único set que no usa
     formularios, y sacale el código que nombra el módulo: los casos de Inicio con autoría y con planillas también lo usan.
     Actualizá sus menciones en el mapa de instrucciones y en `configurar-ia.md`.
5. Opcional: en `Infrastructure.csproj` quedan sin uso `ClosedXML`, `SkiaSharp.HarfBuzz` y
   `HarfBuzzSharp.NativeAssets.Linux`, con sus `PackageVersion` de `Directory.Packages.props`. `SkiaSharp` y `OpenMcdf`
   se quedan: los usan las miniaturas y la inspección de archivos del núcleo.
6. **Conservá todas las migraciones anteriores**: nombran las entidades con cadenas y compilan sin el módulo. Generá
   `RemoveBusinessForms` sobre el código ya borrado, antes de levantar la app:

   ```bash
   dotnet restore ArquitecturaBase.slnx
   dotnet ef migrations add RemoveBusinessForms --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=build;Database=build;Username=build;Password=build" --ConnectionStrings:cache "localhost:6379"
   ```

   `dotnet ef` lee el proyecto antes de compilarlo: en un clon sin `obj`, sin el `restore` no lo encuentra.

   `Up` suelta primero las claves foráneas entre tablas del módulo y después borra sólo sus 26 tablas:
   `FormDefinitions`, `FormDraftChanges`, `FormFolders`, `FormFolderMemberships` y las 22 `BusinessForm*` (`ActionRuns`,
   `CaptureReceipts`, `CatalogItems`, `Catalogs`, `Documents`, `Fields`, `MissingRequests`, `MissingTopics`, `PdfDesigns`,
   `PersonAttributes`, `PersonData`, `PublicationOperations`, `RecordFiles`, `RecordReferences`, `RecordResults`,
   `RecordValues`, `Records`, `RetiredIdentities`, `RuleSets`, `Suggestions`, `Tasks` y `Versions`). Si aparece otra
   tabla, algo se borró de más.

   `Down` las recrea desde el modelo, que no tiene las dos claves diferibles de `BusinessFormCatalogItems`: EF no modela
   la diferibilidad, y las crearon a mano `AddUniqueManualCatalogItemPositions` y `DeferCatalogItemCodeUniqueness`. Sus
   `Down` las borran, así que bajar la base más allá de ellas falla, y los tests de migraciones de IA lo hacen.
   Agregalas al final del `Down`:

   ```csharp
   // EF no modela la diferibilidad: las dos claves de las opciones se recrean como en
   // AddUniqueManualCatalogItemPositions y DeferCatalogItemCodeUniqueness, cuyos Down las borran.
   migrationBuilder.Sql("""
       ALTER TABLE "BusinessFormCatalogItems"
       ADD CONSTRAINT "AK_BusinessFormCatalogItems_CatalogId_Position"
       UNIQUE ("CatalogId", "Position") DEFERRABLE INITIALLY DEFERRED;
       ALTER TABLE "BusinessFormCatalogItems"
       ADD CONSTRAINT "AK_BusinessFormCatalogItems_CatalogId_Code"
       UNIQUE ("CatalogId", "Code") DEFERRABLE INITIALLY DEFERRED;
       """.ReplaceLineEndings("\n"));
   ```

   Después compilá, revisá que el modelo no tenga cambios pendientes y corré todos los tests:

   ```bash
   dotnet build ArquitecturaBase.slnx
   dotnet ef migrations has-pending-model-changes --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api -- --environment Development --ConnectionStrings:appdb "Host=build;Database=build;Username=build;Password=build" --ConnectionStrings:cache "localhost:6379"
   dotnet test
   ```

   Aplicala con el bundle antes de la imagen nueva ([despliegue](despliegue.md#el-orden-bundle-después-imagen)), con
   las rutas de formularios de la versión vieja detenidas. Los permisos `form*` quedan sin efecto, porque el catálogo ya
   no los tiene. Los archivos de las respuestas quedan en el almacenamiento privado con su metadata del núcleo
   (`StoredFiles`): la migración no los borra.

## Front

Quitá `src/features/business-forms` y lo que la compone afuera:

- En `src/app/routes.tsx`, las rutas de `/formularios…`, `/catalogos…` y `/datos-de-personas`; el `authenticatedShell`
  queda en `<AppLayout />`. Borrá `src/app/FormOverviewShell.tsx`, `FormOverviewCount.tsx`, `UserDataSectionShell.tsx`,
  `FormMenuShell.tsx` y `FormMenuReader.tsx` (el menú de las carpetas, CP5-A) con sus tests, y
  `src/layouts/formsOverviewNavigation.ts`, el contexto del contador. `FormMenuReader.tsx` importa la feature: si queda,
  el build se rompe.
- En `src/layouts/navigation.ts`, el panel `forms`, `isFormsPath` y el ítem `navigation.personData`. En `Sidebar.tsx`,
  el panel, `FormsOverviewLink` y el resaltado de `/formularios…` en `NavItem`; en `Breadcrumbs.tsx`, las migas del PDF,
  de las respuestas y de la carga. Las carpetas en el menú (CP5-A) llegan por un contrato neutral, que sin
  `FormMenuShell` queda vacío: compila igual, pero borralas también. Son `src/layouts/folderMenuNavigation.ts`, en
  `Sidebar.tsx` `folderMenuModel`, los menús propios debajo de Inicio, sus grupos en los paneles y el aviso «No pudimos
  cargar tu menú.», en `Breadcrumbs.tsx` las migas de un formulario abierto desde el menú, y `src/shared/ui/menuIcons.ts`
  con `Sidebar.folders.test.tsx`. `TreeList` es una pieza común y queda. En `common.json`, sus ocho textos: `navigation.forms`, `designForms`, `catalogs`,
  `missingForms`, `formOverview`, `responses`, `personData` y `layout.sidebar.closeForms`. En los tests,
  `Sidebar.permissions.test.tsx` entero y los casos de formularios de `Sidebar.test.tsx`, `Breadcrumbs.test.tsx` y
  `navigation.test.ts`.
- Los namespaces `forms`, `formEntries` y `formFolders`, en español y en inglés, y `layout.sidebar.closeMenu` de
  `common.json`.
- En `src/test` y `src/test/visual`, los archivos que importan la feature, y `src/test/responseAssistanceFixture.ts`.
- En `src/test/httpCohort.ts`, las 17 familias de formularios: todas menos `formFiles`, que usa WhatsApp, las
  `whatsapp*` y `aiAssistant`. Con ellas se van sus capturas de `src/test/fixtures` (`catalogs-http` y las 13 que
  empiezan con `forms-`), sus líneas de `.gitattributes` y sus filas de `httpCohort.test.ts`, cuyo caso sintético pasa
  de `workflow` a `whatsappForms`.
- Las escenas `catalog*.spec.ts`, `form*.spec.ts`, `personData.spec.ts` y `responses*.spec.ts`, con las cuatro
  capturas `catalog-field-in-use-*`. Algunas no son de formularios: `formActionMessages`, `formReplies` y
  `formSharedControls` prueban WhatsApp y el catálogo de componentes, y se quedan; de `formFiles` sale sólo el caso F1,
  y de `catalogMultipleChoice` queda sólo el del chat (CA5). De `comparisons.spec.ts` sale la revisión del borrador.
- En `src/test/visual/fixtures.ts`, los fixtures, escenarios, permisos y manejadores de formularios, y `forms` de
  `installedFeatures`. Los escenarios `forms-chat-*` y `file-cards` son de WhatsApp y se quedan. En `preview.tsx`, la
  ruta `/formularios/catalogos-preview`.

WhatsApp no importa la feature: su envío de formularios consulta la API del canal, que sin fuente no ofrece ninguno.
`installedFeatures` deja de traer `forms` y `form-files`. `src/shared/hooks/userDataSectionContext.ts` es el punto de
extensión neutral de Editar usuario: se queda y, sin proveedor, no agrega nada. Las piezas de `src/shared` que sólo
usaba la feature, como `FilterPanel`, `PasteGrid`, `StepList`, `PhonePreview` o `useCurrentBreadcrumbParent`, son de
la biblioteca común y se conservan. Los tests del núcleo que usan rutas o permisos de formularios sólo como datos, como
`ProtectedRoute`, `httpClient` o `useFilters`, pasan sin cambios.

```powershell
npm run build
npm run lint
npm run test
npm run test:visual
```

## Documentación

Borrá `docs/features/formularios.md`, `docs/plans/formularios/` y los planes de formularios que queden en `docs/plans`.
Después arreglá lo que los enlaza: `grep -rlE "formularios\.md|plans/formularios" --include=*.md .`, sin
`docs/history` ni `docs/reviews`. Actualizá `AGENTS.md`, el [índice de premisas](../architecture/premisas-de-la-plantilla.md)
y el [mapa de instrucciones](../architecture/mapa-de-instrucciones.md), sin las filas de las carpetas borradas. En el
front, quitá las filas de `src/features/business-forms` de su
[mapa de instrucciones](../../../ArquitecturaBaseFront/docs/mapa-de-instrucciones.md), su párrafo de
`src/features/AGENTS.md`, la entrada Formularios de `docs/architecture.md`, las capturas borradas de
`src/test/AGENTS.md` y los diseños de `docs/design` que enlazan escenas borradas. Seguí la
[auditoría documental](auditar-documentacion.md).

## Comprobaciones registradas

El 2026-11-03 y el 2026-11-04 se siguió esta guía en copias descartables: un `git archive` del commit, sin tocar los
checkouts. Después el módulo sumó `BusinessFormRecordResults`, `FormFolders` y `FormFolderMemberships`, y quedó en 26
tablas: la próxima prueba de fuego vuelve a comprobar la migración y cuántas claves foráneas suelta.

En el backend, sobre `d558c421` y con los tests corregidos de abajo:

- Build sin advertencias.
- `RemoveBusinessForms` con las 23 tablas y las cuatro claves foráneas, más las dos claves diferibles del `Down`.
- `has-pending-model-changes` sin cambios.
- Domain 384/384, Application 1535/1535 y arquitectura 177/177.
- Integración con Docker: 1826 casos, 1745 correctos y 81 omitidos, que son las composiciones de WhatsApp que
  necesitan el módulo. Ninguna falla.

`dotnet ef` arma la Api en Development, donde el contenedor se valida al construirse, y no faltó ningún servicio. La
corrida anterior, del 2026-11-03, también compiló sin los tres paquetes opcionales.

Para llegar ahí se corrigieron tests que daban por instalado el módulo: `ControllerInputContractTests` y el control de
`ErrorCodeTests` en el núcleo, los controles de formularios de `MigrationsTests` (pasaron a `FormMigrationsTests`), 44
composiciones de WhatsApp que no declaraban la guarda y el inventario de rutas de WhatsApp. Con el módulo, la suite
completa sigue en verde: Domain 530/530, Application 3385/3385, arquitectura 195/195 e integración 2456/2456.

En el front, sobre `94b11e5f`:

- `npm run build` sin advertencias.
- `npm run lint`: oxlint sin diagnósticos en 511 archivos y arquitectura 53/53.
- `npm run test`: 161 archivos y 1842 tests.
- `npm run test:visual`: 314/314 en las 24 escenas que quedan.
