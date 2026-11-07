# Renglones · Etapa 3: plan de implementación

> **Para quien lo ejecute:** se trabaja tarea por tarea, con TDD (el test en rojo primero) y en `main`, sin worktrees
> ([AGENTS.md](../../AGENTS.md)). Es el modo liviano: nueve tareas y el cierre, una revisión al terminar el backend y la
> suite completa sólo al cerrar la etapa. Las casillas (`- [ ]`) se marcan al terminar cada paso. Lo que sigue un patrón
> existente nombra su archivo de referencia; las reglas nuevas van escritas acá.

**Objetivo:** que una respuesta con renglones se lea entera. Abarca la columna del bloque en la planilla, «Filtros» por
algún ítem con sus conteos, la vista rápida, la pantalla completa, la tarjeta del chat de WhatsApp, la hoja del bloque en
el Excel y la tabla del PDF (RG4-A). Las reglas («Algún ítem con…») son de la Etapa 4.

**Fuentes:**

- el [plan de renglones](2026-11-03-renglones-en-formularios.md): RG4-A; RG5-A, en lo que dice de «Filtros»; RG12-A;
  «Lecturas y reglas», y la Etapa 3;
- el [diseño registrado en el front](../../../ArquitecturaBaseFront/docs/design/renglones-en-formularios.md), que manda en
  pantallas y textos;
- el tablero RG4-A del lienzo provisorio [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M),
  versión 55.

Los textos dicen «ítem» (RG12-A): «En algún ítem de Artículos» y la columna «Ítem» del Excel. El código y los documentos
siguen diciendo renglón (`rows`).

**Tecnología:** .NET 10, xUnit v3, Testcontainers con PostgreSQL, ClosedXML y SkiaSharp; en el front, React, Zod, Vitest
con MSW y Playwright.

**Depende de la Etapa 2**, «cargar y guardar los renglones», que trabaja en otro chat. Leer renglones necesita lo que los
guarda: los valores con su renglón, su migración y la captura. No se programa hasta que el backend de esa etapa esté
commiteado.

**Visto bueno:** el usuario aprobó este plan el 2026-11-07, con lo que el tablero no fija resuelto como dice abajo.

---

## Lo que pone la Etapa 2

Quedó así en 5ae579b4, e5e1d4d1 y 6c1c484e. Acordado el 2026-11-07 con la sesión de la Etapa 2. Está en la carpeta de notas compartidas
(`renglones-etapa-3-lectura-2026-11-07.md` y la respuesta, `renglones-etapa-2-modelo-2026-11-07.md`) y en su
[plan](2026-11-07-renglones-etapa-2-implementacion.md), en «Cómo se guardan los renglones» y «Las lecturas hasta la
Etapa 3».

- **`ValuesJson`:** la entrada del bloque es `{ fieldId: <bloque>, value: [ [ {fieldId, value}, … ], … ] }`, con cada
  ítem en la forma del nivel de arriba y en orden.
  - Una pregunta sin respuesta no tiene entrada en su ítem, y un ítem vacío es `[]`.
  - Las preguntas `repeated` no tienen entrada arriba.
  - Un bloque sin ítems es `[]`: la app y WhatsApp siempre lo mandan.
- **`BusinessFormRecordValue.Row`:** `int` de 0 a 99, como `Position`, y 0 fuera del bloque. El índice único
  `(RecordId, FieldId, Row, Position)` sirve a los `EXISTS` por ítem, que correlacionan por `Row` entre los valores de las
  preguntas del bloque.
- **Las referencias:** `Row` va en la clave (única por `RecordId, FieldId, Row, ItemId`) y en `FormRecordReferenceRow`
  (`int`, omitido en 0 al serializar). El mismo ítem del catálogo puede ir en dos renglones.
- **La lectura de los ítems:** la Etapa 2 crea `StructuredRecordReadRow(Values)` y `FormRecordPresentationPolicy.Rows(...)`,
  porque la corrección abre los ítems. Devuelve un ítem por renglón, con todas las preguntas del bloque de su versión en
  orden, null en las vacías y la referencia de su renglón. Esta etapa la usa en `Project` para el valor `group`.
- **Hasta esta etapa,** `Project` y el PDF van sin el bloque ni sus preguntas, y las columnas de las preguntas del bloque
  que conserva c9db6d2c dicen «—». Así ningún commit intermedio rompe la planilla, el detalle ni el chat del front, que
  rechazan el tipo `group` en zod. Esta etapa prende el bloque con los contratos nuevos, backend y front a la vez.
- **Turnos:** la Tarea 2 de la Etapa 2 toca `FormRecordPresentationPolicy`, `StructuredRecordReadModels` y
  `SkiaFormPdfRenderer`. Esta etapa los toca después del commit de su backend, que la Etapa 2 avisa por mensaje.

## Cómo se lee el bloque

### La lectura neutral

En `Application/Models/StructuredData/StructuredRecordReadModels.cs`, con lo que crea la Etapa 2:

- el valor del bloque es un `StructuredRecordReadValue` con `Type = "group"`;
- su `Value` es la lista de ítems que da `FormRecordPresentationPolicy.Rows(...)`, `StructuredRecordReadRow(Values)`, uno
  por renglón y en orden; sin ítems, la lista vacía;
- cada ítem trae todas las preguntas del bloque de su versión, en su orden, decodificadas como hoy: con `Value` null si
  quedó sin respuesta y con las referencias de catálogo de su renglón;
- las preguntas `repeated` no van sueltas en `Values`: están adentro del bloque.

La usan el detalle y el chat de WhatsApp (sin campos del catálogo, como hoy), y en la Etapa 4 los mensajes de las reglas.
Las sugerencias ya se quedan sólo con los valores escalares, así que el bloque no les llega.

### La planilla

En `FormResponsePresentationPolicy`:

- **La columna.** El bloque tiene una columna `group` en su lugar, con su nombre, desde qué versión existe y
  `options: []`. Suma `questions`: las columnas de sus preguntas con la forma de siempre (tipo, etiqueta, versión,
  opciones, `valueType` y `fields`). Se unen por FieldId entre las versiones que tuvieron ese bloque, primero en el orden
  de la activa. `questions` se omite fuera de un bloque.
- **Las preguntas del bloque** dejan de tener columna propia, que hoy conservan desde c9db6d2c. Las que pasaron al bloque
  con identidad nueva conservan su columna vieja, como pregunta retirada (RG6-A).
- **La celda:**
  - `NotAsked` si su versión no tenía el bloque;
  - `Unanswered` sin ítems, con `null` o `[]`;
  - `Answered` con la lista de ítems, `[{ cells: [...] }]`. Cada ítem trae una celda por pregunta de `questions`, en ese
    orden, con los estados de siempre; `NotAsked`, la que el bloque de su versión no tenía.
- **El orden.** Ni el bloque ni sus preguntas se ordenan: un `sortFieldId` de cualquiera de los dos es
  `Forms.Record.FilterInvalid`, como en «elegir varias».
- **«3 · …».** Lo arma el front: la cantidad en gris y los valores de la primera pregunta de cada ítem, separados con «, »
  y formateados como cualquier celda, en una línea que se corta con «…». Cuenta la primera celda de cada ítem que no sea
  `NotAsked`; si quedó sin respuesta, ese ítem no suma valor.

### «Filtros» y sus conteos

En `FormRecordFilterPolicy` y `FormResponseReader.Filters` y `.Facets`:

- **Una condición** sobre una pregunta del bloque usa los operadores de su tipo y se cumple si algún ítem la cumple. Sobre
  el bloque mismo es `FilterInvalid`.
- **Varias condiciones** sobre preguntas del mismo bloque buscan un mismo ítem que las cumpla todas. Es la regla de RG5-A,
  que «Filtros» comparte con las reglas. Se resuelve con un `EXISTS` sobre los valores del bloque correlacionado por
  `Row`, sin multiplicar la respuesta por sus ítems.
- **La presencia.** `answered`, `unanswered` y `notAsked` sobre una pregunta del bloque miran la respuesta entera (algún
  ítem con valor, ninguno, o la versión no la tenía) y no entran en el mismo ítem. El front ya no los manda desde PL2-A;
  quedan para las reglas.
- **Los conteos** (`filter-counts`) aceptan las preguntas del bloque: su `fieldId` está en `questions`. Cada opción cuenta
  las respuestas con algún ítem que la tenga y que cumpla, en ese mismo ítem, las otras condiciones del bloque. Así el
  conteo de una opción es lo que da la planilla al elegirla. La faceta de un catálogo sale de las referencias de todos
  los ítems.
- **La búsqueda y el total.** «Buscar en textos largos» incluye los textos largos de los ítems, y el total sigue contando
  respuestas.

### El detalle: vista rápida y pantalla completa

El detalle (`GET /api/forms/records/{id}`) trae el bloque en `values`, con la lectura neutral.

- **Vista rápida (D2, TL1-A).** Bajo el nombre del bloque va cada ítem con su número y el valor de la primera pregunta en
  negro. Debajo van los demás valores, alineados: la pregunta en gris y el valor, con la línea a la izquierda, como los
  campos de un catálogo. Una opción con campos del catálogo los muestra como hoy.
- **Pantalla completa (DC1).** Una tabla en la columna del valor, con `DataTable` dentro de `WideTable`: el número del
  ítem a la derecha y una columna por pregunta, con la línea entre columnas y los números a la derecha. En el celular se
  desplaza de costado dentro de su caja.
- **Sin ítems,** «Sin respuesta», como cualquier pregunta.

### La tarjeta del chat de WhatsApp

- **El backend** sólo suma la prueba y la captura: `BusinessFormRecordSource` ya proyecta con la lectura neutral sin
  campos del catálogo, y `WhatsAppChatQueryService` pasa el valor tal cual.
- **La tarjeta** (`FormReplyValues`) pone el nombre del bloque y un ítem por línea: «1. GLIFOSATO 64% · 200 · Litros ·
  Sí». Los valores van en el orden de las preguntas y formateados como hoy, sin los que quedaron sin respuesta, como «Así
  llega». El bloque cuenta como un dato más para plegar la tarjeta.

### El Excel

En `FormExportService` y `ClosedXmlFormWorkbookWriter`:

- **Las hojas.** `FormWorkbookRequest` pasa a llevar hojas, `FormWorkbookSheet(Name, Headers, Rows)`; el export de
  opciones de un catálogo arma una.
- **«Respuestas»** queda como hoy, con la cantidad de ítems en la columna del bloque: un entero, vacía sin ítems y «—» si
  su versión no tenía el bloque.
- **La hoja del bloque.** Se suma una por bloque exportado, en el orden de las columnas. Un bloque entra con «Todas las
  columnas» o si su columna está visible.
  - Sus columnas son «Número», «Ítem» y sus preguntas. Cada pregunta lleva su columna «· valor» si es de un catálogo con
    tipo, y los campos del catálogo que elige `catalogFields`: con null, todos; con pares, sólo esos.
  - Lleva una fila por ítem, en el orden de las respuestas de «Respuestas» y después en el de los ítems.
  - Los tipos, la precisión, el «—» y la celda vacía, como hoy.
- **El nombre de la hoja** es el del bloque, sin los caracteres que Excel no admite y hasta 31. Si queda vacío o igual a
  «Respuestas», se llama «Ítems» («Items»); si dos se llaman igual, la segunda suma « (2)».
- `catalogFields` acepta también los pares de una pregunta del bloque.

### El PDF

En `FormPdfDesignPolicy`, `FormPdfPreviewIssuer` y `SkiaFormPdfRenderer`:

- **El diseño elige el bloque entero.**
  - El diseño inicial y una respuesta sin diseño guardado llevan el bloque y no sus preguntas.
  - Una pregunta `repeated` no es una pregunta conocida para un diseño nuevo. Un diseño ya guardado la conserva y el
    renderer la ignora.
  - Un idioma está completo si también lo están las preguntas del bloque y sus opciones.
  - En el front, la página del diseño lista el bloque con su nombre y no sus preguntas. Las opciones de las reglas, que
    también usa, siguen trayendo todas para la Etapa 4.
- **La tabla.** Con el bloque elegido, el PDF pone su nombre como título y una tabla:
  - el número del ítem y una columna por pregunta de su versión;
  - la línea entre columnas, los números a la derecha y el texto partido en renglones dentro de su celda;
  - si no entra, sigue en la página siguiente con el encabezado repetido;
  - una opción de catálogo, como hoy: texto, valor y campos de papel dato;
  - sin ítems, «Sin respuesta».
- **La vista previa del diseño** suma dos ítems con los valores de ejemplo de cada pregunta.

## Lo que el tablero no fija

Se resuelve con la opción más simple que cumple el plan y las reglas vigentes:

- **«Filtros» con PL1-A y PL2-A.** RG4-A se dibujó con las casillas y el select «Respuesta», que ya no están.
  - La lista de la izquierda tiene el nombre del bloque como encabezado, que no se elige, y sus preguntas debajo con
    sangría.
  - El panel de una pregunta del bloque lleva su nombre como título y, debajo en gris, «En algún ítem de Artículos», como
    lo dibujó RG4-A. Una con opciones usa el combo de PL2-A; texto, número y fecha filtran como hoy.
  - `FilterPanel` (`shared/ui`) suma el encabezado de grupo y esa línea gris, con su ficha en el estándar visual.
  - El chip dice la pregunta, como hoy.
- **Los ítems con campos del catálogo** se muestran como cualquier opción, en D2, DC1, el PDF y el Excel.
- **El número del ítem** va sin encabezado en DC1 y en el PDF, como en el tablero. En DC1 lleva «Ítem» para lectores de
  pantalla.
- **Las columnas de la tabla del PDF:** el número, angosto; las preguntas se reparten el resto por igual.
- **La tarjeta del chat** no tiene tablero: su forma salió del plan y quedó fijada al registrar el diseño («como Así
  llega»).

## Compilar y probar

Con la salida propia, para no chocar con otras sesiones ni con la app abierta:

```bash
dotnet build ArquitecturaBase.slnx --artifacts-path C:/Users/ezequ/source/repos/ArquitecturaBase/.artifacts/renglones-e3
```

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --artifacts-path C:/Users/ezequ/source/repos/ArquitecturaBase/.artifacts/renglones-e3 -- --filter-class "<Namespace.Clase>"
```

Otras sesiones trabajan en el mismo checkout: se preparan sólo los archivos y los fragmentos propios. Con la Etapa 2, no
se editan los mismos archivos a la vez. El usuario prueba con la app levantada: el `src` del front se edita sólo con su
permiso.

## Tareas

### Tarea 1 · La lectura del bloque y la planilla

- [x] Esperar el commit del backend de la Etapa 2, revisar `git status` y ajustar «Lo que pone la Etapa 2» a lo que
  quedó.
- [x] Tests en rojo:
  - en `FormRecordRowsPresentationTests`, la clase que creó la Etapa 2 con el mismo escenario: `Project` con el valor
    `group` y los ítems de `Rows` en orden, también con el mismo ítem del catálogo en dos; las preguntas `repeated` fuera
    de `Values`; un bloque sin ítems, y una versión sin el bloque;
  - `FormResponsePresentationPolicyTests`: la columna `group` con `questions` unidas entre versiones, sin columnas
    sueltas para sus preguntas, y las celdas con sus estados. Reemplaza
    `The_block_of_rows_has_no_column_and_its_questions_keep_theirs`;
  - `FormRecordFilterPolicyTests`: ordenar por el bloque o por una de sus preguntas es `FilterInvalid`.
- [x] El bloque en `Project`, con `Rows`; `Questions` en `FormResponseColumn` y
  `FormResponseRowCells` en `FormResponseModels`, con `WhenWritingNull`; `Columns` y `Cells`; el orden en
  `FormRecordFilterPolicy`.
- [x] Para que ningún commit rompa lo que ya proyecta, en la misma tarea:
  - «Respuestas» del Excel ya pone la cantidad de ítems (`FormExportPolicy`); la hoja del bloque sigue en la Tarea 3;
  - un mensaje de una regla que nombra el bloque lo deja vacío, como hasta ahora (`FormActionMessagePolicy`), hasta que
    las reglas lean ítems en la Etapa 4;
  - la captura real de la Etapa 2 y la composición de WhatsApp con ítems esperan el bloque en la lectura.
- [x] Integración (`FormRowsReadHttpTests`, como `FormMultipleChoiceReadHttpTests`): la planilla y el detalle de un
  formulario con bloque, es y en, con capturas `rows-sheet-{es,en}` y `rows-detail-{es,en}` para la cohorte
  `forms-responses-http`. Reemplaza
  `FormResponseHttpTests.A_published_block_of_rows_without_responses_lists_an_empty_sheet_without_a_block_column`. La
  lista del exportador suma cada captura en la tarea que la produce, para que la corrida de otra sesión no falle.
- [x] Commit: `feat(formularios): la planilla y el detalle leen el bloque de renglones`.

### Tarea 2 · «Filtros» por algún ítem

- [x] Tests en rojo:
  - `FormRecordFilterPolicyTests`: aceptar condiciones sobre las preguntas del bloque y rechazar las del bloque, también
    las de presencia;
  - `FormResponseServiceTests`: los conteos aceptan una pregunta del bloque y no el bloque;
  - `FormRowsQueryTests`, sin abrir una base como `FormMultipleChoiceQueryTests`: dos condiciones del bloque son un
    `EXISTS` sobre la respuesta con la segunda buscada en el `Row` de la primera, sin `JOIN`;
  - integración con PostgreSQL, en `FormRowsReadHttpTests`: una condición en cualquier ítem; dos en el mismo ítem contra
    dos en ítems distintos; la presencia por respuesta; el conteo de una opción en el mismo ítem; la búsqueda en los
    textos largos de los ítems, y el total que cuenta respuestas, sin repetir una respuesta por sus ítems.
- [x] `FormResponseReader.Filters`: las condiciones con valor sobre preguntas del bloque van juntas en un `EXISTS` por
  `Row` (`ItemConditions` y `SameItem`). `.Facets`: el conteo de una opción con las otras condiciones del bloque en el
  mismo ítem. `FormResponseService.CountFacetsAsync`: buscar el `fieldId` también en `questions`.
- [x] Capturas `rows-counts-{es,en}`.
- [x] Commit: `feat(formularios): «Filtros» busca un ítem del bloque que cumpla todo`.

### Tarea 3 · La hoja del bloque en el Excel

- [ ] Tests en rojo:
  - `FormWorkbookWriterTests`: varias hojas, y los nombres sin caracteres prohibidos, cortados y sin repetir;
  - `FormExportPolicyTests` y `FormExportServiceTests`: la cantidad en «Respuestas», las columnas y filas de la hoja del
    bloque, «—» y la celda vacía, `catalogFields` y el bloque oculto con «sólo las visibles».
- [ ] `FormWorkbookSheet` en `FormExportModels`, el escritor, `FormExportService`, el export de opciones de un catálogo
  (`FormCatalogExportService`) e «Ítem» en `Responses.resx` y `.en.resx`.
- [ ] Capturas `rows-export-{es,en}.xlsx`, como `multi-export`.
- [ ] Commit: `feat(formularios): el Excel suma una hoja por bloque`.

### Tarea 4 · La tabla del PDF

- [ ] Tests en rojo:
  - `FormPdfDesignPolicyTests`: el diseño inicial y `Resolve` sin las preguntas del bloque, las preguntas conocidas y el
    idioma completo con las preguntas del bloque;
  - `FormMultipleChoicePdfPreviewTests` como modelo: la vista previa con dos ítems;
  - técnicos sin Docker (`FormPdfRendererTests`): la tabla con su título y encabezado, una tabla que sigue en la página
    siguiente con el encabezado repetido, los números a la derecha y una celda larga partida.
- [ ] `FormPdfDesignPolicy`, `FormPdfPreviewIssuer` y `SkiaFormPdfRenderer`, con `WriteTable` en su `DocumentLayout`.
- [ ] Exportar los PDF técnicos y revisar los PNG de Poppler. Capturas `rows-preview-{es,en}.pdf` con su companion
  `.http.json`, en `forms-pdf-http`, como `multi-preview`.
- [ ] Commit: `feat(formularios): el PDF muestra el bloque en una tabla`.

### Tarea 5 · La respuesta con ítems en el chat

- [ ] Integración en `WhatsAppFormReplyAppPreviewTests`, o el productor de `whatsapp-forms-http` que corresponda: el hilo
  trae la respuesta con el bloque y sus ítems, sin campos del catálogo, es y en, con captura `reply-rows`.
- [ ] Si algo del canal no deja pasar el bloque, arreglarlo ahí, sin que WhatsApp nombre a BusinessForms.
- [ ] Commit: `test(whatsapp): la respuesta con renglones llega al chat con sus ítems`, o `feat` si hubo código.

### Tarea 6 · Revisión del backend

- [ ] Un revisor sobre las tareas 1 a 5 y sus correcciones. Unitarios de formularios y ArchitectureTests enteros antes de
  cada commit; la integración que tocan, con PostgreSQL.

### Tarea 7 · El front: contrato y modelo

- [ ] Esperar que la sesión de la Etapa 2 haya commiteado el front que toque los mismos archivos, revisar `git status` y
  pedirle permiso al usuario para editar el `src` del front.
- [ ] `api/responses.ts`:
  - el tipo `group` en columnas, celdas y valores, con `questions` sólo en el bloque y sólo con los tipos que entran en
    un ítem;
  - la celda con `[{ cells }]` y el valor del detalle con `[{ values }]`, con estados coherentes.
- [ ] `responses.ts`: la cantidad y los primeros valores de una celda del bloque. Las funciones que buscan una pregunta
  por FieldId (normalizar la consulta, reemplazar una condición, los chips) miran también las preguntas del bloque.
- [ ] En WhatsApp, el bloque en `api/formReplies.ts` y `lib/formReplyValue.ts`.
- [ ] Tests sobre DTO sintéticos: `api/rowsRead.test.ts`, `responses.test.ts` y `formReplies.test.ts`.
- [ ] Commit en el front: `feat(formularios): el contrato de la planilla y el detalle lleva el bloque`.

### Tarea 8 · El front: la planilla y «Filtros»

- [ ] La columna del bloque en `FormResponsesPage` («3 · …», sin ordenar) y en «Columnas».
- [ ] `FilterPanel` con el encabezado de grupo y la línea gris, más su ficha en el estándar visual.
- [ ] `ResponseFiltersMenu` con las preguntas del bloque debajo de su nombre y «En algún ítem de {bloque}».
- [ ] Textos es y en en `forms.json`.
- [ ] Tests: `FormResponsesPage.rows.test.tsx`, `ResponseFiltersMenu.rows.test.tsx` y el de `FilterPanel`.
- [ ] Escena visual `responsesRows`: la planilla y «Filtros», es y en, a 1440, 390 y 320.
- [ ] Commit en el front: `feat(formularios): la planilla muestra el bloque y filtra por algún ítem`.

### Tarea 9 · El front: vista rápida, pantalla completa, diseño del PDF y chat

- [ ] Los ítems en D2 (`ResponseRowsValue`, con la forma de `CapturedFields`) y la tabla de DC1, desde `ResponseAnswer`.
- [ ] La página del diseño del PDF con el bloque y sin sus preguntas.
- [ ] La tarjeta del chat (`FormReplyValues`) con un ítem por línea.
- [ ] Tests: `ResponseDetail.rows.test.tsx`, `FormPdfDesignPage.test.tsx` y `FormReplyValues.rows.test.tsx`.
- [ ] Escenas `responsesRows` (D2 y DC1), `chatForms` con el bloque y `formPdf` con el bloque, es y en, a 1440, 390 y 320.
- [ ] Commit en el front: `feat(formularios): el detalle y el chat muestran los ítems del bloque`.

### Tarea 10 · Cierre

- [ ] Docs:
  - [formularios](../features/formularios.md), en la planilla, el Excel, el PDF y la persistencia;
  - [WhatsApp](../features/whatsapp.md), en la tarjeta de la respuesta;
  - los `AGENTS.md` de Services, Persistence, Pdf y sus tests;
  - en el front, los de `components`, `api` y `whatsapp`, y «Lo que cambia de lo que hay» del diseño de renglones;
  - este plan y el de renglones.
- [ ] Exportación de las cohortes que toca:
  - `forms-responses-http`, de 55 a 63 archivos;
  - `forms-pdf-http`, de 32 a 36, con `rows-preview` y sus companions;
  - `whatsapp-forms-http`, con `reply-rows` es y en.
  Con sus listas en `export-responses-http.ps1` y su autotest, y en el front `rowsReadHttpContract.test.ts`, con las
  listas de los tests de contrato que cuentan capturas. Se exporta desde una copia: el exportador corre la suite entera y
  escribe en el front.
- [ ] Verificación de cierre:
  - build sin advertencias;
  - `dotnet test` completo con Docker, en tandas;
  - `vitest` y `test:visual` del front, en tandas;
  - el recorrido es y en a 1440, 390 y 320;
  - `aspire stop` si se levantó la app.
