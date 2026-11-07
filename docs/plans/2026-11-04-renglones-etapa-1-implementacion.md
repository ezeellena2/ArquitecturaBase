# Renglones · Etapa 1: plan de implementación

> **Para quien lo ejecute:** se trabaja tarea por tarea, con TDD (el test en rojo primero) y en `main`, sin worktrees
> ([AGENTS.md](../../AGENTS.md)). Es el modo liviano: diez tareas, una revisión al cerrar el backend y la suite completa sólo
> al cerrar la etapa. Las casillas (`- [ ]`) se marcan al terminar cada paso. Lo que sigue un patrón existente nombra su
> archivo de referencia; las reglas nuevas van escritas acá.

**Objetivo:** que el editor arme el bloque de renglones y que se publique en WhatsApp con una pantalla por renglón, sin
cargar respuestas todavía (eso es la Etapa 2).

**Fuentes:** el [plan de renglones](2026-11-03-renglones-en-formularios.md) (decisiones RG1-A a RG7-A, el modelo y la
Etapa 1) y el [diseño registrado en el front](../../../ArquitecturaBaseFront/docs/design/renglones-en-formularios.md), que
manda en pantallas y textos.

**Tecnología:** .NET 10, xUnit v3, Testcontainers con PostgreSQL; en el front, React, Zod, Vitest con MSW y Playwright.

---

## Cómo se representa el bloque

El borrador sigue siendo una lista plana de preguntas. Casi todo lo que la recorre (identidades, ledger de retiradas,
traducciones faltantes, catálogos, `Matches`, aceptación, el diff, los guards del front y el tope de 40) mira sólo el nivel
de arriba, así que una lista anidada obligaría a tocarlo todo.

- **El bloque** es una pregunta `type: "group"` con nombre (`label`), ayuda (`help`) y `rows: { minRows, maxRows }`. No
  lleva opciones, largo, catálogo, archivos, selección ni `repeated`. `required` es verdadero si y sólo si `minRows > 0`,
  como en «elegir varias».
- **Sus preguntas** son las que siguen, contiguas, con `repeated: true`: es la casilla «Se carga varias veces». Entran
  texto corto, texto largo, número, fecha, sí o no y elegir una (propia o de catálogo).
- **Los campos nuevos** (`rows` y `repeated`) son opcionales y se omiten cuando son nulos (`JsonIgnore(WhenWritingNull)`),
  en la entrada, en el esquema canónico, en el sellado y en el puerto neutral: los formularios sin bloque conservan sus
  bytes, huellas y hashes. `repeated: false` se normaliza a nulo.
- **Cuántas preguntas:** el bloque no cuenta como pregunta y sus preguntas sí. El tope de 40, `QuestionCount`, la cuenta de
  la lista y «Preguntas» en «Qué se publica» cuentan las que no son `group` («10, cinco en el bloque»).
- **Identidad:** entrar o salir del bloque cambia `repeated` y crea otra identidad, como cambiar el tipo. El front manda la
  pregunta con `localId` nuevo y `replacesFieldId` (precedente: `changeQuestionMultiplicity` del front).
  `FormDraftPolicy.Resolve` acepta `replacesFieldId` cuando cambia el tipo, el catálogo **o** `repeated`, y rechaza conservar
  un `fieldId` con otro `repeated`.

## Reglas nuevas del borrador

En `SaveFormDraftRequestValidator`, todas `ContentInvalid` en su ruta:

1. `group` entra en la lista de tipos (y en `FormQuestionIdentity.Create` y `FormDraftPolicy.TypeOf`).
2. A lo sumo un `group` (ruta `draft.fields[i].type` del segundo).
3. `rows` es obligatorio en un `group` y nulo en cualquier otro tipo: `0 ≤ minRows ≤ maxRows ≤ 100`, `maxRows ≥ 1`, y
   `required` ⇔ `minRows > 0` (ruta `draft.fields[i].rows`, como `selection`).
4. Un `group` no lleva opciones, `maxLength`, `source`, `files`, `selection` ni `repeated`.
5. `repeated: true` sólo en los tipos que entran al bloque, y sólo si hay un `group`. Las preguntas con `repeated` forman un
   tramo contiguo que empieza justo después del `group` (ruta `draft.fields[i].repeated`).
6. Un bloque sin preguntas se guarda; no se publica (tarea 3).

## Publicación y WhatsApp

- **Problema** `Forms.Publication.GroupEmpty`, en el `group` sin preguntas: «Agregá al menos una pregunta al bloque.».
- **Aviso** `Forms.Publication.RowsOverChannelLimit`, con `limit` y `maximum`, cuando `maxRows` pasa el tope de WhatsApp.
  Sigue el precedente de los avisos de opciones por persona, que BusinessForms calcula con el tope del canal. El tope vive en
  una constante neutral, `StructuredFormLimits.MaxRows = 10` (Application/Models/StructuredData), que usan el aviso y el
  compilador. Si la verificación con Meta lo baja, se cambia ahí.
- **«Pasan a renglones»:** `DiffAsync` empareja cada pregunta nueva con `repeated` con una quitada del nivel de arriba con el
  mismo tipo, el mismo catálogo y el mismo nombre en el idioma original. Las devuelve en `diff.movedToRows` y las saca de
  `added` y de `removed`. Si además se renombró, queda como quitada y agregada.
- **Puerto neutral:** `StructuredFormQuestion` suma `Rows` y `Repeated` (opcionales); se mapean en
  `FormPublicationSnapshotPolicy.ChannelRequest` y en `FormPublicationDispatchService.CreateChannelRequests`.
- **Compilador, revisión `flow-static-7.3-r10`:** sin bloque, los mismos bytes que r9. Con bloque:
  - las pantallas siguen el orden de las preguntas: lo de antes del bloque (partido en cada foto o documento, como hoy), una
    pantalla por renglón hasta `min(10, maxRows)` y lo de después;
  - cada renglón lleva las preguntas del bloque con los mismos nombres de componente y el título «Artículos · renglón 2»
    (`FlowTexts.RowTitle`);
  - desde el mínimo, el renglón termina con «¿Agregás otro renglón?» (RadioButtonsGroup `field_<bloque>`, «Sí»/«No»
    obligatoria) y un `If` con un `Footer` por rama: con «Sí», `navigate` al renglón siguiente; con «No», `navigate` a lo
    de después o `complete`. El último renglón no pregunta;
  - el Flow declara `routing_model` con cada `navigate` de cada pantalla, las dos ramas del `If` incluidas (2026-11-06): el
    que Meta deduce de los `navigate` deja afuera la rama «No»;
  - las claves de la respuesta de un renglón son `field_<pregunta>_r<n>` y las de «¿Agregás otro?», `field_<bloque>_r<n>`.
    Cada `complete` junta lo visitado con `${screen.<pantalla>.form.<campo>}`;
  - el presupuesto de componentes pasa a contarse por pantalla (50); el de bytes cuenta cada pregunta del bloque una vez por
    renglón, con 46 bytes por clave de renglón, y con bloque usa 64 KiB (RG7-A);
  - más de 26 pantallas es `UnsupportedConfiguration`;
  - el prellenado (`WhatsAppFlowPrefillPolicy`) sigue las pantallas nuevas y no prellena el bloque; `PrefillRevisions`
    suma r10. El mapeo de diagnósticos de Meta (`WhatsAppFlowDispatchService.Map`) reconoce las pantallas de renglón.
- **Webhook:** `WhatsAppWebhookReader` acepta claves `field_<N>_r<0..10>` (`_r0` es «¿Agregás un renglón?», RG8-A), sube
  el tope de campos a 450 cuando hay claves de renglón y usa 64 KiB (`WhatsAppFlowResponseLimits.HasRows`). Decodificar los
  renglones es de la Etapa 2: hasta entonces, la respuesta de un Flow con bloque se rechaza por claves que el mapping no
  conoce.

## Antes de dos tareas, preguntar al usuario

- **Tarea 4:** un bloque con mínimo 0 en WhatsApp (no se dibujó). Resuelto el 2026-11-04 con RG8-A: una pantalla con el
  nombre del bloque y «¿Agregás un renglón?» antes del primero; desde el primero, cada renglón pregunta.
- **Tarea 7:** «Renglones» se elige en el desplegable «Tipo de respuesta» (no hay menú en «Agregar pregunta», como dibujó
  RG1-A), y una opción nativa no muestra el motivo «Ya hay un bloque de renglones.». Resuelto el 2026-11-04 con RG9-A (lienzo,
  versión 84): la opción apagada lleva el motivo después de «·», como «Foto · …», y en una pregunta del bloque también foto y
  documento («· No se carga varias veces»).

## Por decidir (la revisión del backend, 2026-11-04)

- **Un mínimo mayor que 10** (por ejemplo, de 15 a 20): WhatsApp carga a lo sumo 10 renglones, menos que el mínimo, y el
  aviso de publicar sólo habla del máximo. Hay que decidir qué hace publicar y contra qué valida la Etapa 2.
- **Una lista dependiente (CW7) que filtra por una pregunta del bloque:** publicar la acepta desde una pregunta de fuera del
  bloque, y no queda claro de qué renglón toma la respuesta.

## Compilar y probar

Con la salida propia, para no chocar con otras sesiones ni con la app abierta:

```bash
dotnet build ArquitecturaBase.slnx --artifacts-path C:/Users/ezequ/source/repos/ArquitecturaBase/.artifacts/renglones
```

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --artifacts-path C:/Users/ezequ/source/repos/ArquitecturaBase/.artifacts/renglones -- --filter-class "<Namespace.Clase>"
```

Otras sesiones trabajan en el mismo checkout: se preparan sólo los archivos y los fragmentos propios, y el front espera a
que la sesión de las listas dependientes commitee lo suyo.

## Tareas

### Tarea 1 · El bloque en el borrador

- [x] Tests en rojo en `tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/FormGroupDraftTests.cs` y en
  `tests/ArquitecturaBase.Domain.UnitTests/Modules/BusinessForms/FormDraftModelTests.cs`: cada regla de arriba, la
  normalización de `repeated: false` y de `rows`, la identidad nueva al entrar o salir del bloque, el rechazo de conservar
  el `fieldId` con otro `repeated`, y que un borrador sin bloque conserva su JSON y su huella.
- [x] `FormSelectionSettings` como precedente: `FormRowSettings(int MinRows, int MaxRows)` y `bool? Repeated` en
  `FormFieldInput` y `CanonicalFormField` (`Models/FormDraftModels.cs`), con `WhenWritingNull`.
- [x] Reglas en `SaveFormDraftRequestValidator`, `TypeOf`/`Normalize`/`Resolve` en `FormDraftPolicy`, la lista de
  `FormQuestionIdentity.Create`, y la cuenta sin `group` en `FormDraftService` (`SetDraft`).
- [x] Commit: `feat(formularios): el borrador arma un bloque de renglones`. Verde en una copia de HEAD (la sesión de la IA
  tenía tests a medias): 35 nuevos, Domain 594, ArchitectureTests 198 y los unitarios de formularios 1934.

### Tarea 2 · El contrato HTTP del borrador

- [x] Integración en rojo (`FormGroupDraftHttpTests`): guardar con un bloque, la respuesta con `rows` y `repeated`, pasar
  una pregunta guardada al bloque y sacarla (identidad nueva, la anterior retirada), y las rutas de los errores.
- [x] `FormRowSettingsHttpRequest` y los parámetros `Rows` y `Repeated` en `FormFieldHttpRequest`, `FormDraftRequestMapping`
  y la lista de `FormDraftRequestMappingTests`.
- [x] Captura para el front (`FORMS_HTTP_CONTRACT_OUTPUT`) en `forms-http/rows/draft.json` y su lugar en
  `export-responses-http.ps1`.
- [x] Commit: `feat(formularios): el contrato del borrador lleva el bloque de renglones`. Verde: ArchitectureTests 198 y
  los dos HTTP nuevos con PostgreSQL.

### Tarea 3 · Chequeo, aceptación y puerto neutral

- [x] Tests en rojo: el problema del bloque vacío, el aviso del máximo, `movedToRows` (incluido el caso renombrado), la
  aceptación que registra el bloque y sus preguntas en `BusinessFormField`, el sellado con `rows` y `repeated`, el
  `QuestionCount` sin el bloque y los dos mapeos al puerto neutral.
- [x] Códigos en `FormPublicationErrors` con sus textos en `Errors.resx` y `.en.resx`; `StructuredFormLimits`; el problema y
  el aviso en `FormPublicationPolicy` (`ValidateRows`, `RowWarnings`, sólo con un canal instalado); `MovedToRows` en
  `FormPublicationDiffResponse`, omitido si está vacío; `Rows` y `Repeated` en `StructuredFormQuestion`.
- [x] Integración (`FormGroupPublicationHttpTests`) y captura `forms-http/rows/publication-check.json`; el autotest del
  exportador pasa a 66 cuerpos.
- [x] Commit: `feat(formularios): publicar revisa el bloque de renglones y dice qué pasa a renglones`. Verde: 11 unitarios
  nuevos, Application 3485, ArchitectureTests 198 y el HTTP nuevo con PostgreSQL.

### Tarea 4 · El compilador con renglones (r10)

- [x] Preguntar al usuario por el mínimo 0, con tablero si hace falta: RG8-A (lienzo, versión 82).
- [x] Tests en rojo en `WhatsAppFlowRowsCompilerTests`: las pantallas y sus títulos, el `If` con los dos pies, el último
  renglón sin pregunta, las claves `_r<n>`, cada `complete`, el presupuesto por pantalla y de bytes con 64 KiB, y que sin
  bloque r10 da los mismos bytes que r9 (fixture nuevo `flow-static-7.3-r10-{es,en}.json`). «Más de 26 pantallas» no
  puede darse: los renglones no llevan archivos y los archivos suman diez como mucho, así que un Flow llega a 23
  pantallas; no se agregó control.
- [x] `WhatsAppFlowProfile` (r10 y `PrefillRevisions`), `WhatsAppFlowCompiler`, `WhatsAppFlowScreenBuilder`,
  `WhatsAppFlowPrefillPolicy`, `WhatsAppFlowDispatchService.Map`, `FlowTexts` es/en, y los tests que fijan r9 como actual.
  `WhatsAppFlowScreenBuilder.Plan` ordena las pantallas para las tres piezas; las guías de WhatsApp ya dicen r10.
- [x] Commit: `feat(whatsapp): el Flow suma una pantalla por renglón con «¿Agregás otro renglón?»`. Verde en la copia de
  HEAD: 11 casos nuevos, Application.UnitTests 3515 y ArchitectureTests 198; la integración compila (corre al cierre).

### Tarea 5 · El webhook con renglones

- [x] Tests en rojo en `WhatsAppWebhookReaderTests` y `WhatsAppFlowResponseLimitsTests`: claves `_r<n>` aceptadas, el tope
  de campos con renglones, 64 KiB con renglones y 16 KiB sin ellos, y claves mal formadas rechazadas.
- [x] `WhatsAppFlowResponseLimits.HasRows` y `IsFieldKey` (el formato de la clave, de `_r0` a `_r10`), y `WhatsAppWebhookReader`.
- [x] Commit: `feat(whatsapp): el webhook acepta las respuestas con renglones`. Verde en la copia de HEAD: 17 casos nuevos
  de límites, `WhatsAppWebhookReaderTests` 52 (sin base), Application.UnitTests 3532 y ArchitectureTests 198.
- [x] Revisión del backend (un revisor) y correcciones. Sin cambios de bytes en lo que ya existe. Corregido: el asistente y
  las sugerencias no ofrecen ni mapean las preguntas del bloque; la lectura de formularios de la IA no cuenta el bloque entre
  las 40; el tope de renglones del Flow pasa a `WhatsAppFlowProfile.MaxRows`, parte de r10 (un test lo ata a
  `StructuredFormLimits.MaxRows`, el del aviso); `WhatsAppRowsCompositionTests` publica 40 preguntas y un bloque por el canal
  real y verifica la proyección sellada; las guías dejan de decir «`Group` reservado» y «lo nuevo compila r9». Con el chequeo
  de publicar se exporta también `rows/publication-check-moved.json` (c077bd52). Lo que el revisor dejó para Meta va en la
  Tarea 9; las dos preguntas de diseño, en «Por decidir».

### Tarea 6 · El front: modelo, guards y contrato

- [x] Esperar el commit de la sesión de las listas dependientes y revisar `git status`.
- [x] `model.ts`, `api/response.ts` (tipo `group`, `rows`, `repeated`, `RowsOverChannelLimit`, `movedToRows`),
  `draftSession.ts` y `bindResult`, `editorModel.ts`, `validationPaths.ts`, con sus tests sobre DTO sintéticos
  (`api/rows.test.ts`, como los de varias). `rowsHttpContract.test.ts` sobre las capturas `rows/*` queda para la exportación
  conjunta del cierre, que corre la suite entera. También: el bloque no es condición de reglas ni sugerencias, y las
  sugerencias no mapean sus preguntas (igual que el backend).
- [x] Commit en el front: `feat(formularios): el modelo del editor lleva el bloque de renglones` (e994f743). `tsc`, lint y
  los tests del área en verde; tres que se cortaron por tiempo con la máquina cargada pasan solos.

### Tarea 7 · El front: el editor

- [x] Preguntar al usuario por el motivo de «Renglones» en el desplegable: RG9-A.
- [x] Un módulo puro `rowsQuestions.ts` (como `selectionQuestions.ts`): crear el bloque, marcar y desmarcar «Se carga varias
  veces» con identidad nueva, mover el bloque con sus preguntas, el «Agregar pregunta» de adentro y los tipos permitidos.
  Lo que los tableros no fijaron, con la opción más simple: el tacho del bloque lo quita con sus preguntas (pide confirmar y
  dice cuántas), un bloque con preguntas no cambia de tipo (su desplegable se apaga con el motivo) y uno vacío sí, y una
  pregunta del bloque que cambia a otro tipo que entra en un renglón sigue en el bloque.
- [x] `QuestionEditor` (casilla `CheckboxField` con su (i); editor del bloque con nombre, ayuda, mínimo y máximo, y la (i)
  del máximo; RG9-A en el desplegable), la lista en `FormEditorPage` con el recuadro y la numeración 5.1, 5.2…, el conteo
  sin el bloque y los textos es/en.
- [x] Tests (`rowsQuestions.test.ts`, `QuestionEditor.rows.test.tsx`, `FormEditorRows.test.tsx`) y escena visual
  `formRows` (`forms-rows`), es/en a 1440, 390 y 320, sin scroll de la página. La vista previa todavía dibuja el bloque
  como un campo: es de la Tarea 8.
- [x] Commit en el front: `feat(formularios): el editor arma el bloque de renglones`.
- [ ] RG10-D (2026-11-06, en la verificación con Meta): «Agregar bloque» en la franja «Preguntas», el bloque como una
  pregunta más y con «Bloque» de nombre al crearlo, también desde «Se carga varias veces»; «Renglones» sale de «Tipo de
  respuesta». Se programa al terminar la prueba con Meta, con la app apagada: textos es/en, `QuestionEditor`,
  `FormEditorPage`, `rowsQuestions.ts`, sus tests, la escena `formRows` y el `AGENTS.md` del área.

### Tarea 8 · El front: vista previa y publicar

- [x] `FormPreview` sigue las pantallas de WhatsApp (`previewScreens` en `rowsQuestions.ts`): lo de antes, «¿Agregás un
  renglón?» con mínimo 0, un renglón de ejemplo («Artículos · renglón 1», con «¿Agregás otro renglón?» si pregunta) y lo de
  después; con el bloque o una de sus preguntas abierta va al renglón. `PublicationCheckDialog` con «Pasan a renglones»,
  «Preguntas» sin el bloque y el aviso del máximo con «Ir a la pregunta»; el problema del bloque vacío usa el texto del
  backend, como los demás. La revisión lado a lado (LR2-A) compara el bloque por mínimo y máximo, y cada pregunta del
  bloque dice que se carga varias veces.
- [x] Tests (`FormPreview.rows.test.tsx`, `PublicationCheckDialog.rows.test.tsx`, `draftReviewLines.test.ts`,
  `rowsQuestions.test.ts`) y la escena `formRows` con el renglón en la vista previa. La escena del diálogo de publicar con
  el bloque va con la exportación conjunta del cierre, sobre los cuerpos reales `rows/publication-check*.json`, como
  `rowsHttpContract.test.ts`.
- [x] Commit en el front: `feat(formularios): la vista previa y publicar muestran el bloque de renglones` (e6f3eefb).
  `tsc`, lint, los tests tocados y la escena en verde.

### Tarea 9 · Verificación con Meta

- [ ] Con el visto bueno del usuario, publicar un formulario de prueba con un bloque de 10 renglones y comprobar: el límite
  de 10 ramas, lo que llega de los renglones no visitados, las opciones de un catálogo en cada renglón y una respuesta de
  unos 40 KB. Son tres variantes, porque r10 usa estructuras que ningún perfil anterior publicó (un `If` con `Footer` dentro
  del `Form`, pantallas terminales que navegan en una rama y completan en la otra, y el mismo `name` en varias pantallas):
  con preguntas después del bloque, con el bloque al final y con mínimo 0. Si Meta objeta el `If` dentro del `Form`, las
  pantallas de renglón van sin `Form` (en 7.3 es opcional).
  - 2026-11-06, en curso con el visto bueno del usuario («Completa ahora»). Variante 1, preguntas después del bloque (de 1
    a 10, con un catálogo) y dos títulos de sección: Meta la aceptó y quedó publicada y verificada. Acepta el `If` con
    `Footer` dentro del `Form`, el mismo `name` en las diez pantallas, las referencias a renglones que pueden no visitarse
    y los `TextSubheading` de los títulos (CW15-B). Lo que llega del celular se lee de la respuesta guardada, sin valores.
  - 2026-11-06, el celular mostraba «Se produjo un error» al responder «¿Agregás otro renglón?», y la vista oficial
    interactiva (VO1-A), con «No» en el renglón 1: «Can't perform a transition from [FORM_A] to [FORM_K], because it doesn't
    satisfy provided routing_model». El Flow no declaraba `routing_model` y Meta lo deduce de los `navigate` sin la rama
    `else`. Ahora r10 con bloque lo declara con las dos ramas (sin bloque no cambia). Falta republicar la prueba (una
    versión nueva del formulario) y probar «Sí» y «No» en la vista oficial y en el celular.
  - 2026-11-07, con la versión 4 (con `routing_model`): el usuario completó el formulario entero en el celular, con un
    renglón y «No». Llegaron 7 claves y 415 bytes: las del renglón 1 con `_r1` (incluida `field_<bloque>_r1`), las de fuera
    del bloque y el `flow_token`. De los renglones no visitados no llega ninguna clave: ni vacía ni con `${…}`. La app
    la marca `Unrecognized` («no pudimos relacionar con un envío»), porque `ProjectionIsWellFormed` no acepta el tipo
    `group`: guardar la respuesta con renglones es de la Etapa 2, que además tiene que cambiar ese aviso.
  - 2026-11-07, más pruebas:
    - Otra corrida de la versión 4 en el celular, con «Sí», «Sí» y «No»: 15 claves y 886 bytes, con `_r1` a `_r3` en cada
      pregunta del bloque y en `field_<bloque>`.
    - Variante 2 (bloque al final, de 1 a 3) y variante 3 (mínimo 0, de 0 a 2, con un texto largo y una pregunta
      después): Meta las aceptó y quedaron publicadas y verificadas, con el `routing_model` declarado, las pantallas
      terminales que navegan en una rama y la pantalla «¿Agregás un renglón?».
    - El usuario las recorrió en la vista oficial interactiva sin errores.
    - Falta la respuesta de unos 40 KB.
- [ ] Si algo no alcanza, bajar el tope o volver a 16 KiB (RG7-C) y anotarlo en el plan. El tope está sellado en r10
  (`WhatsAppFlowProfile.MaxRows`, con `StructuredFormLimits.MaxRows` igual): mientras r10 no salga de esta etapa se cambian
  los dos y se republica la prueba; después, otro tope es otra revisión, y el webhook sigue aceptando `_r10` de lo publicado.

### Tarea 10 · Cierre

- [ ] Docs: [formularios](../features/formularios.md) (la regla del tipo reservado y el bloque),
  [WhatsApp](../features/whatsapp.md) (r10 y el tope), los `AGENTS.md` del módulo y del compilador, el plan de renglones y
  el diseño del front (el desplegable en lugar del menú).
- [ ] Exportación conjunta y la cohorte `forms-http` del front, que suma `rows/draft.json`, `rows/publication-check.json` y
  `rows/publication-check-moved.json` (de 64 a 67 capturas): `httpContract.test.ts` (la lista de nombres y el título) y
  `multipleChoiceHttpContract.test.ts` cuentan las de renglones, y se suman `rowsHttpContract.test.ts` y la escena del
  diálogo de publicar con el bloque. La sesión de las listas dependientes regeneró sólo la cohorte de la carga (front
  0f0f037a) y avisó que, con las capturas nuevas, esos dos tests fallan hasta este paso.
- [ ] Verificación de cierre: build sin advertencias, `dotnet test` completo con Docker en tandas, `vitest` y visuales del
  front en tandas, y `aspire stop` si se levantó la app.
