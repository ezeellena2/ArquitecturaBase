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

- [x] Tests en rojo:
  - `FormWorkbookWriterTests`: varias hojas con su orden, sus nombres y sus celdas tipadas;
  - `FormExportPolicyTests`: el nombre de la hoja sin caracteres prohibidos, cortado, «Ítems» y sin repetir (lo decide
    Application, que tiene los textos; el escritor sólo escribe);
  - `FormRowsExportTests`, con dos versiones del bloque: la cantidad en «Respuestas», las columnas y filas de la hoja del
    bloque, «—» y la celda vacía, `catalogFields` y el bloque oculto con «sólo las visibles»;
  - integración con PostgreSQL, en `FormRowsReadHttpTests`: lo que se lee en las dos hojas, es y en.
- [x] `FormWorkbookSheet(Headers, Rows, Name)` en `FormExportModels`, el escritor, `FormExportService`, el export de
  opciones de un catálogo (`FormCatalogExportService`) e «Ítem» e «Ítems» en `Responses.resx` y `.en.resx`. Los tests
  que leían la única hoja pasan a `Sheets[0]`.
- [x] Capturas `rows-export-{es,en}.xlsx`, como `multi-export`.
- [x] Commit: `feat(formularios): el Excel suma una hoja por bloque`.

### Tarea 4 · La tabla del PDF

- [x] Tests en rojo:
  - `FormPdfDesignPolicyTests`: el diseño inicial y `Resolve` sin las preguntas del bloque, las preguntas conocidas y el
    idioma completo con las preguntas del bloque;
  - `FormRowsPdfPreviewTests`, como `FormMultipleChoicePdfPreviewTests`: la vista previa con dos ítems;
  - técnicos sin Docker (`FormPdfRendererTests`): la tabla que arma `Table` (título, encabezados, columnas numéricas y
    celdas, con la vacía), el bloque que suma su tabla y la pregunta del bloque elegida sola que no sale, una tabla de 60
    ítems en tres páginas y un bloque sin ítems. El encabezado repetido, los números a la derecha y la celda partida no
    se pueden leer desde el test (Skia no deja texto que se pueda buscar): se revisaron en los PNG.
- [x] `FormPdfDesignPolicy`, `FormPdfPreviewIssuer` y `SkiaFormPdfRenderer`, con `WriteTable` en su `DocumentLayout`.
- [x] Exportar los PDF técnicos y revisar los PNG (PyMuPDF, y `pdftotext -layout` de Poppler para el encabezado en cada
  página): `block-es`, `block-long-en` (tres páginas, con el encabezado arriba de cada tramo) y `block-empty-es`
  («Artículos · Sin respuesta»). Capturas `rows-preview-{es,en}.pdf` con su companion `.http.json`, en `forms-pdf-http`,
  como `multi-preview`.
- [x] Commit: `feat(formularios): el PDF muestra el bloque en una tabla`.

### Tarea 5 · La respuesta con ítems en el chat

- [x] Integración en `WhatsAppRowsCompositionTests`, la composición de la Etapa 2 que ya manda el formulario por el chat y
  recibe tres ítems por el webhook firmado: el hilo trae la respuesta con el bloque y sus ítems, es y en, con captura
  `reply-rows` en `whatsapp-forms-http`. Que los ítems no llevan los campos del catálogo lo prueba
  `FormRecordRowsPresentationTests` con la lectura del chat (`includeCatalogFields: false`).
- [x] El canal deja pasar el bloque tal cual: no hizo falta código.
- [x] El exportador suma `reply-rows` a las respuestas que une y a su inventario, y su autotest pasa a 63 respuestas,
  36 del PDF y 78 de WhatsApp. El autotest ya fallaba antes de esta etapa en «Definitions» (pide 67 y `forms-http` tiene
  70): queda como pendiente de quien sumó esas capturas.
- [x] Commit: `test(whatsapp): la respuesta con renglones llega al chat con sus ítems`.

### Tarea 6 · Revisión del backend

- [x] Un revisor sobre las tareas 1 a 5 y sus correcciones. Unitarios de formularios y ArchitectureTests enteros antes de
  cada commit; la integración que tocan, con PostgreSQL.
  - La lógica central quedó bien: el `EXISTS` por ítem, los conteos, la planilla, el detalle, el PDF y el chat, y nada
    cambia en un formulario sin bloque.
  - Un bug real, corregido: el nombre de la hoja podía terminar en apóstrofe, partir un emoji o ser «History», y ClosedXML
    rechazaba el libro entero con un 500. `SheetName` limpia hasta que no cambia y corta por caracteres.
  - Corregidos también: la tabla del PDF reserva su primer ítem entero con el nombre y el encabezado, para no dejarlos
    solos al pie; el test de la tabla larga pide sus tres páginas y el del bloque sin ítems, su renglón; la guía de
    Services ya no dice que el bloque queda afuera.
  - Quedan como límites conocidos, sin caso real hoy: un bloque con muchísimas preguntas angosta las columnas del PDF; un
    ítem partido entre páginas no repite su número, y la hoja del bloque no tiene tope propio de filas (el presupuesto de
    30 segundos corta el export).

### Tarea 7 · El front: contrato y modelo

- [x] Esperar que la sesión de la Etapa 2 haya commiteado el front que toque los mismos archivos, revisar `git status` y
  pedirle permiso al usuario para editar el `src` del front. El usuario lo dio el 2026-11-07. Hecha en aba058bc: el
  contrato, el modelo (`responseRowsSummary`, `responseQuestionColumns`) y WhatsApp (`formatFormReplyItem`), con
  `rowsRead.test.ts`, `formRepliesRows.test.ts` y los casos de `responses.test.ts` y `formReplyValue.test.ts`; los fixtures
  que arman el detalle desde la planilla convierten la celda del bloque (`detailValue`).
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

- [x] La columna del bloque en `FormResponsesPage` («3 · …», sin ordenar, en `ResponseValue`) y en «Columnas», que ya la
  trae como una columna más.
- [x] `FilterPanel` con el encabezado de grupo (`group`) y la línea gris (`note`), descriptos en su guía de `shared/ui`.
- [x] `ResponseFiltersMenu` con las preguntas del bloque debajo de su nombre y «En algún ítem de {bloque}».
- [x] Textos es y en en `forms.json` (`responses.inSomeItem`).
- [x] Tests: `FormResponsesPage.rows.test.tsx`, `ResponseFiltersMenu.rows.test.tsx` y `FilterPanel.test.tsx`.
- [ ] Escena visual `responsesRows`: la planilla y «Filtros», es y en, a 1440, 390 y 320. Va con la corrida de
  `test:visual` del cierre.
- [x] Commit en el front: `feat(formularios): la planilla muestra el bloque y filtra por algún ítem` (8fbd531f).

### Tarea 9 · El front: vista rápida, pantalla completa, diseño del PDF y chat

- [x] Los ítems en D2 (`ResponseRowsList`, con la forma de `CapturedFields`) y la tabla de DC1 (`ResponseRowsTable`, con
  `WideTable`), en `ResponseRows.tsx` y desde `ResponseAnswer`; cada respuesta de un ítem se muestra con `ResponseValue`.
- [x] La página del diseño del PDF con el bloque y sin sus preguntas; una que eligió un diseño viejo aparece como
  «Pregunta retirada», para poder destildarla.
- [x] La tarjeta del chat (`FormReplyValues`) con un ítem por línea.
- [x] Tests: `ResponseAnswer.rows.test.tsx`, el caso nuevo de `FormPdfDesignPage.test.tsx` (escrito después del cambio,
  que era de dos líneas) y `FormReplyValues.rows.test.tsx`.
- [ ] Escenas `responsesRows` (D2 y DC1), `chatForms` con el bloque y `formPdf` con el bloque, es y en, a 1440, 390 y 320.
  Van con la corrida de `test:visual` del cierre.
- [x] Commit en el front: `feat(formularios): el detalle y el chat muestran los ítems del bloque`.

### Tarea 10 · Cierre

- [x] Docs (backend dc4df3a4, front fb538560):
  - [formularios](../features/formularios.md), en la planilla, el Excel, el PDF y la persistencia;
  - [WhatsApp](../features/whatsapp.md), en la tarjeta de la respuesta;
  - los `AGENTS.md` de Services, Persistence, Pdf y sus tests;
  - en el front, los de `components`, `api` y `whatsapp`, y «Lo que cambia de lo que hay» del diseño de renglones;
  - este plan y el de renglones.
- [x] Exportación de las cohortes que toca, en la corrida conjunta que hizo la Etapa 2 después de RG10-D y RG12-A (front
  06666b14), que cambian textos que salen en las capturas: front 60fcff31, sobre el backend 735add83, con 7408 de 7408 en
  la suite completa. Entraron en ese commit los contratos de esta etapa, que pasan con las capturas nuevas (34 de 34):
  - `forms-responses-http`, de 55 a 63 archivos;
  - `forms-pdf-http`, de 32 a 36, con `rows-preview` y sus companions;
  - `whatsapp-forms-http`, con `reply-rows` es y en.
  Las listas están en `export-responses-http.ps1` y su autotest; en el front, `rowsReadHttpContract.test.ts`,
  `formRepliesRowsHttp.test.ts` y los conteos de los tests de contrato que cuentan capturas. Se exporta desde una copia:
  el exportador corre la suite entera y escribe en el front.
- [x] Verificación de cierre (2026-11-07):
  - build sin advertencias: el de la corrida completa, con `TreatWarningsAsErrors`;
  - `dotnet test` completo con Docker, desde una copia de dc4df3a4: 7357 de 7358, y la falla (una prueba de migraciones que
    no esperaba `RejectedRow`) la corrigió la Etapa 2;
  - `vitest` en ocho tandas: 4341 tests. Fallaron los ocho contratos que esperaban la exportación conjunta (pasan desde
    60fcff31), dos de «Agregar bloque» (RG10-D, entonces en curso en la sesión de renglones) y seis que vencen a los
    5 segundos con la máquina cargada y pasan solos;
  - `test:visual`: 992 de 992, con la escena `responsesRows` nueva (la planilla, «Filtros» y la pantalla completa, es y en
    a 1440, 390 y 320). D2, la tarjeta del chat y el diseño del PDF quedan cubiertos por sus tests de componente; sumarlos a
    sus escenas (`responses`, `chatForms`, `formPdf`) queda pendiente;
  - el recorrido con la app real queda para la prueba manual del usuario, que tiene la app levantada; no se levantó Aspire.
