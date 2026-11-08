# Renglones · Etapa 2: plan de implementación («Cargar renglones»)

> **Para quien lo ejecute:** se trabaja tarea por tarea, con TDD (el test en rojo primero) y en `main`, sin worktrees
> ([AGENTS.md](../../AGENTS.md)). Es el modo liviano: diez tareas, una revisión al cerrar el backend y la suite completa sólo
> al cerrar la etapa. Las casillas (`- [ ]`) se marcan al terminar cada paso. Lo que sigue un patrón existente nombra su
> archivo de referencia; las reglas nuevas van escritas acá.

**Objetivo:** que la app guarde los renglones de un bloque, cargados desde la app (RG2-A) y por WhatsApp (RG3-A). Hoy un
formulario con bloque se arma y se publica (Etapa 1), pero la respuesta de WhatsApp queda `Unrecognized`
(`WhatsAppFlowReplyService.ProjectionIsWellFormed` no acepta `group`) y la carga desde la app no conoce el bloque.

**Fuentes:** el [plan de renglones](2026-11-03-renglones-en-formularios.md) (decisiones RG1-A a RG13-A y el modelo), el
[plan de la Etapa 1](2026-11-04-renglones-etapa-1-implementacion.md) (cómo se representa el bloque y lo que llega de Meta),
el [diseño registrado en el front](../../../ArquitecturaBaseFront/docs/design/renglones-en-formularios.md), que manda en
pantallas y textos (RG12-A: «ítem» en lugar de «renglón»), y [elegir primero por un campo](2026-11-07-elegir-primero-por-un-campo.md)
(LD3-A).

**Tecnología:** .NET 10, xUnit v3, Testcontainers con PostgreSQL; en el front, React, Zod, Vitest con MSW y Playwright.

**Visto bueno:** el usuario aprobó este plan el 2026-11-07, con lo que el tablero no fija resuelto como dice abajo.

---

## Decisiones de esta etapa

- **Un mínimo mayor que el tope de WhatsApp: RG13-A, publicar frena** (2026-11-07, elegida con la pregunta del chat, sin
  tablero). Con WhatsApp instalado, un bloque con mínimo mayor que 10 es un problema de publicar, con «Ir a la pregunta»:
  «En WhatsApp se cargan hasta 10 ítems; el mínimo es 15.». Al guardar, la app y WhatsApp validan siempre contra el mínimo
  y el máximo del bloque: ninguna respuesta queda con menos ítems que el mínimo. Descartó que publicar avise y WhatsApp pida
  10 aunque el mínimo sea mayor, y que el editor limite el mínimo a 10 en todos los formularios.
- **Listas dependientes con el bloque: LD3-A** (2026-11-07, ya decidida). Si las dos preguntas están en el bloque, la lista
  usa la respuesta del mismo ítem; una de afuera vale para todos los ítems; y publicar frena (`MatchQuestionInvalid`) si una
  pregunta de afuera depende de una del bloque. En el editor, «Respuesta de» dice «· del mismo ítem» o «· para todos los
  ítems», y en una pregunta de afuera las del bloque salen apagadas con «Está en el bloque». Por WhatsApp, CW7 sigue sin
  mandarse.

## Cómo se guardan los renglones

- **`ValuesJson`.** El bloque tiene su entrada en su lugar, como cualquier pregunta, y su valor es un array con un array
  por ítem. Cada ítem lleva los valores de sus preguntas con la misma forma de arriba (`fieldId` y `value`), así un solo
  decodificador sirve para los dos niveles. Van en el orden de las preguntas y sin las que quedaron sin responder; un ítem
  vacío es `[]`. Las preguntas del bloque no tienen entrada propia arriba. Un bloque sin ítems (mínimo 0) queda como `[]`:
  la app y WhatsApp siempre lo mandan. Como «elegir varias», sólo un envío que lo omite lo deja sin entrada.

  ```json
  [
    { "fieldId": "<proveedor>", "value": "<opción>" },
    { "fieldId": "<bloque>", "value": [
      [ { "fieldId": "<artículo>", "value": "<opción>" }, { "fieldId": "<cantidad>", "value": 200 } ],
      [ { "fieldId": "<artículo>", "value": "<opción>" }, { "fieldId": "<cantidad>", "value": 100 } ]
    ] }
  ]
  ```

- **`BusinessFormRecordValue.Row`**, de 0 a 99 al lado de `Position` y como él no nullable, con default 0: el índice del
  ítem, con 0 en lo que no es del bloque. Una fila por pregunta respondida de cada ítem; el bloque mismo no tiene fila (su
  cantidad de ítems vive en `ValuesJson`). El índice único pasa a `RecordId, FieldId, Row, Position` y sirve a los `EXISTS`
  por ítem de la Etapa 3. La Etapa 3 propuso un `Row` nullable: con null, el índice único dejaría pasar duplicados fuera del
  bloque, y una pregunta ya dice si es del bloque.
- **`BusinessFormRecordReference.Row`**, igual: el mismo artículo puede elegirse en dos ítems, así que el índice único pasa a
  `RecordId, FieldId, Row, ItemId`. `FormRecordReferenceRow` suma `Row` (omitido en 0, para no cambiar los bytes de los
  snapshots de origen) y los lectores lo leen.
- **`BusinessFormCaptureReceipt.RejectedRow`**, nullable, al lado de `RejectedFieldId`: el ítem de una opción dada de baja,
  para que el replay de un rechazo devuelva el mismo error.
- **La migración `AddFormRecordRows`** es aditiva: las dos columnas con default 0 y CHECK 0–99, los dos índices únicos, la
  columna nullable del recibo con su CHECK, y ningún dato reescrito. Su Down toma los locks de respuestas, valores,
  referencias y recibos y frena, antes de cualquier DDL, si hay una respuesta de una versión con bloque, un `Row` mayor que 0
  o un `RejectedRow`: la imagen anterior no sabe leerlos. Los bloques en borradores y versiones ya existían en la imagen
  anterior y no la frenan. Precedente: `AddFormMultipleChoiceValues` y su test.

## La captura

- **El puerto neutral no cambia de forma.** Como «elegir varias», que manda un array JSON en su clave, el bloque va en
  `Answers[<clave del bloque>]` como un array JSON con un objeto por ítem: `{"<clave de la pregunta>": "<valor canónico>"}`,
  con los mismos valores canónicos de arriba (decimal con punto, fecha ISO, `true`/`false`, GUID D) y las claves en el orden
  de las preguntas. Así la huella y el replay los incluyen sin tocar `Fingerprint`, y una captura sin bloque conserva su
  huella (un test lo fija con un valor conocido).
- **La validación** sale del recorder a una política pura, `FormRecordAnswerPolicy` (precedente: `FormCatalogOfferPolicy`),
  que hoy es el `Normalize` privado de `BusinessFormRecordRecorder` más el bloque:
  - la cantidad de ítems entre el mínimo y el máximo del bloque (RG13-A);
  - cada ítem, un objeto sin claves repetidas, sólo con claves de las preguntas de ese bloque, y cada pregunta validada como
    una suelta: obligatoria, tipo, largo y opción ofrecida (la oferta de un catálogo es la misma para todos los ítems);
  - ninguna pregunta del bloque respondida arriba, ni el bloque con otro valor que un array.

  Lo inválido sigue siendo `InvalidResponse`, que no consume número.
- **El recorder** guarda una fila de valor y una referencia por pregunta de cada ítem, con su `Row`. La revisión de
  actividad de las opciones Manual bajo lock es por ítem y su rechazo (`CatalogItemInactive`) lleva `RejectedFieldId` y
  `RejectedRow`; `StructuredRecordCaptureResponse` suma `RejectedRow`, opcional.
- **El error de un ítem en la app** va en `answers.<bloque>.<n>.<pregunta>`, con `n` desde 0, el índice del ítem en el
  envío. Un error de la cantidad de ítems no llega desde la app (el front la revisa antes), así que sigue siendo el
  `InvalidResponse` general.

## WhatsApp

- **`ProjectionIsWellFormed`** acepta un `group` con `Rows` válidos y sus preguntas `Repeated` de los tipos que entran en un
  ítem; el tope de 40 cuenta las preguntas sin el bloque.
- **Un codec puro, `WhatsAppFlowRowsCodec`** (precedente: `WhatsAppFlowValueCodec`), arma los ítems con las claves que manda
  Meta y con `Asks` y `RowCount` del compilador, que lee sin cambiarlo:
  - con mínimo 0, `<bloque>_r0` es obligatoria: `true` sigue al ítem 1 y `false` deja el bloque en `[]`;
  - cada ítem cargado decodifica `<pregunta>_r<n>` con `WhatsAppFlowValueCodec`, como una pregunta suelta;
  - donde el ítem pregunta, `<bloque>_r<n>` es obligatoria y vale `true` o `false`; antes del mínimo, el ítem siguiente
    viene siempre, y el último no pregunta;
  - devuelve `decoded[<bloque>]` con la forma del puerto, y las claves `_r` no pasan sueltas al dueño.
- **Rechazo e ignorados:** es `FlowErrors.InvalidValue` una pregunta obligatoria que falta en un ítem cargado, una respuesta
  a «¿Agregás otro ítem?» que falta o no es `true`/`false`, una clave `_r` de una pregunta de afuera del bloque o en un ítem
  que no pregunta, y una pregunta del bloque sin sufijo. Lo que llegue de los ítems después del «No» (quien volvió con la
  flecha y respondió «No») se ignora: la prueba física confirma con Meta qué llega en ese caso.
- **El aviso.** Una proyección que la app no sabe leer (`ProjectionIsWellFormed` falso) queda `Rejected` con
  `InvalidValue`, con el aviso de siempre («No pudimos guardar la respuesta del formulario.»), en lugar de «no pudimos
  relacionar con un envío», que queda para lo que de verdad no se relaciona (token o sesión).
- El tope de 64 KiB con bloque y las claves `_r0` a `_r10` del webhook ya están (Etapa 1).

## La carga desde la app

- **Lo que se carga** (`GET /api/forms/entries/{id}`): el bloque con `rows: { minRows, maxRows }` y sus preguntas con
  `repeated: true`, en orden, como en el borrador. `FormEntryQuestionResponse` suma los dos, omitidos si son nulos.
- **El envío** (multipart, `answers` en JSON): el bloque va como `{ fieldId: <bloque>, rows: [[{ fieldId, value }, …], …] }`;
  adentro de un ítem sólo `{ fieldId, value }`. `FormEntryAnswer` y `FormEntryAnswerHttpItem` suman `Rows`.
  `FormEntrySendRules.ValidAnswers` acepta hasta 100 ítems de hasta 40 respuestas, sin `rows` anidados ni preguntas
  repetidas en un ítem; `FormEntryRecorder.Prepare` exige `rows` sólo en el bloque y sólo preguntas suyas, y arma la clave
  del bloque con la forma del puerto.
- **La oferta por ítem (LD3-A).** `FormEntryCatalogPolicy.OffersAsync` revisa cada ítem con las respuestas de arriba más las
  del mismo ítem, así una lista del bloque que depende de otra del bloque usa la del mismo ítem y una que depende de una de
  afuera, la de afuera. Junta por pregunta las opciones ofrecidas de todos los ítems en una sola oferta; un error va en
  `answers.<bloque>.<n>.<pregunta>`.
- **Corregir (CW10-A)** abre y guarda los ítems: `GET .../records/{recordId}` devuelve el bloque con `rows`, cada ítem con
  sus respuestas como las de arriba (`FormEntryCorrectionAnswer` suma `Rows`), pasadas a la versión activa: siguen las
  preguntas que siguen en el bloque con el mismo tipo; si el bloque ya no está, no vuelven. `CorrectAsync` usa el mismo
  envío. Sin esto, corregir una respuesta con ítems los perdía.

## Las lecturas hasta la Etapa 3

La Etapa 3 muestra los ítems (RG4-A). Hasta entonces, nada que lea respuestas se rompe con ellos:

- `FormRecordPresentationPolicy.Project` deja afuera el bloque y sus preguntas: el detalle, el chat, la planilla, el Excel y
  los mensajes de las reglas no los muestran, y las columnas de las preguntas del bloque, que conserva la planilla
  (c9db6d2c), dicen «—». El contrato de lectura del front no conoce `group`, y un valor así rompía la pantalla entera.
- `FormRecordPresentationPolicy.Rows` decodifica los ítems de un bloque con su `Row` y sus referencias, con el mismo
  `Decode`, en la lectura neutral que propuso la Etapa 3: `StructuredRecordReadRow(Values)` en
  `StructuredRecordReadModels`, un ítem por renglón y en orden, con todas las preguntas del bloque de su versión y `Value`
  null en las que quedaron sin responder. Lo usa la corrección y lo va a usar la Etapa 3 en `Project`.
- El PDF deja afuera el bloque y sus preguntas aunque el diseño los tenga.
- Las reglas no miran el bloque (es de la Etapa 4) y los filtros de la planilla sobre una pregunta del bloque ya buscan en
  cualquier ítem (`EXISTS` sobre los valores).

## Publicar

- **RG13-A:** `FormPublicationErrors.MinRowsOverChannelLimit`, un problema en `draft.fields[i].rows` con `limit`, sólo con
  un canal instalado, junto a `RowWarnings`. El backend dice «En WhatsApp se cargan menos ítems que el mínimo del bloque.»
  y el front arma el texto con los números, como el aviso del máximo: «En WhatsApp se cargan hasta 10 ítems; el mínimo es
  15.».
- **LD3-A:** `FormCatalogMatchPolicy.ValidateQuestions` recibe la pregunta y da `MatchQuestionInvalid` si una pregunta de
  afuera del bloque filtra por una del bloque.

## El front

- **El modelo** (`entryForm.ts`): `EntryDraft.rows` guarda, por bloque, la lista de ítems, cada uno con una clave estable y
  sus valores y opciones. La tabla arranca con el mínimo de ítems, con la opción única de los filtros elegida en cada uno.
  Funciones puras para agregar, quitar y mover; `validateEntry` revisa cada ítem como preguntas sueltas y la cantidad
  («Agregá al menos 1 ítem.»); `toEntrySend`, `carryEntryDraft`, `keepForNextEntry` («Mantener» conserva los ítems),
  `correctionDraft`, `entrySignature` y `entryDraftKey` los incluyen; `entryDependencyAnswers` y `clearDependentChoices`
  resuelven por ítem (LD3-A); el bloque ocupa el renglón entero de su zona y sus preguntas no se dibujan sueltas.
- **El contrato** (`api/entries.ts`): `group`, `rows` y `repeated` en la pregunta, `rows` en el envío y en la corrección.
- **Dos piezas nuevas de `shared/ui`**, con su ficha en el estándar visual (vacía, con el máximo, con errores y en una
  columna):
  - la tabla de carga, sobre `Table`: el número a la izquierda y a la derecha (como todo número), una columna por pregunta
    con su etiqueta arriba, los controles de siempre en cada celda, ↑ ↓ y el tacho en rojo con `IconButton`, el mensaje de
    cada ítem debajo de su fila, «Agregar ítem» al pie con `DisabledReason` («Hasta 20 ítems.») y el desplazamiento de
    costado de `WideTable`;
  - la versión en recuadros, para el panel junto a la planilla y el celular: «Ítem 2», ↑ ↓ y «Quitar» en la banda, y las
    preguntas una debajo de otra.
- **La página, el panel y el celular** (`FormEntryForm`, `useEntryEditor`, `FormEntryPage`, `FormEntryPanel`,
  `FormEntryCorrectionPage`): el bloque a todo el ancho de «Datos» donde lo puso el autor; al enviar, la celda con un
  problema en rojo, su mensaje debajo del ítem con el nombre de la pregunta («Cantidad: completá esta respuesta.») y el foco
  en la primera; los errores del servidor por ítem; «Mantener». «Enviada» (CW5) muestra el bloque en una línea, como va a
  quedar la columna de la planilla (RG4-A): «3 · GLIFOSATO 64%, ATRAZINA 50%, UREA GRANULADA…».
- **Publicar y el editor:** el texto de RG13-A en `publicationPresentation`, y LD3-A en `CatalogQuestionFilters`.

## Resueltas por defecto

Con la opción más simple que cumple el plan y las reglas:

- Corregir carga y guarda los ítems; «Antes: …» no se muestra adentro del bloque.
- Hasta la Etapa 3 las lecturas no muestran el bloque ni sus preguntas, y el PDF tampoco.
- Por WhatsApp, lo de los ítems después del «No» no se guarda, y el aviso del chat nombra la pregunta sin el ítem.
- Una versión nueva mientras se carga conserva los ítems si el bloque sigue, con las preguntas que siguen y el mismo tipo.
- «Agregar ítem» lleva el foco a la primera pregunta del ítem nuevo; ↑ ↓ lo dejan en el mismo botón del ítem movido; quitar
  lo lleva al ítem que queda en su lugar o, si no hay, a «Agregar ítem». Los textos que los tableros no fijan (el motivo
  del tacho apagado, los nombres de ↑ ↓ para el lector de pantalla) salen del tablero RG2 y, si no están, se escriben con
  «ítem».
- El error del servidor en una pregunta de un ítem va a su celda, como el de una pregunta suelta va a su campo.

## Coordinación

- **El chat de la Etapa 1** (`local_81a26003-86cd-4de5-bda0-9bec94d80664`) cierra RG10-D y RG12-A con la app apagada: hasta
  que avise que está commiteado no se tocan el editor del bloque, `WhatsAppFlowScreenBuilder`, `FlowTexts`, la vista previa
  ni los textos `forms.json` del front. El codec sólo lee `Asks` y `RowCount`. Su cierre exporta las capturas `rows/*`
  (de 64 a 67): las de esta etapa se suman después.
- **El chat de la Etapa 3** ([su plan](2026-11-07-renglones-etapa-3-implementacion.md), con el visto bueno del usuario)
  lee este modelo y programa después del backend de esta etapa. Lo acordado está arriba: cada ítem con la forma de arriba,
  `[]` sin ítems, los dos `Row` no nullables, `StructuredRecordReadRow` y `Rows` desde esta etapa, y `Project` y el PDF sin
  el bloque hasta que la Etapa 3 los prenda. La respuesta a su propuesta queda en la carpeta de notas compartidas
  (`renglones-etapa-2-modelo-2026-11-07.md`). Las dos tocan `FormRecordPresentationPolicy`, el renderer del PDF y
  `StructuredRecordReadModels`, pero en turnos.
- **Elegir primero por un campo** (LD1-A, sesión «IMPORTANTE Filtro») está programando su backend en el mismo checkout,
  sobre los archivos de la carga y de publicar (`FormEntryModels`, sus validadores, `FormEntryCatalogPolicy`,
  `FormPublicationErrors`, `Errors.resx`). Las tareas 1 a 3 no los tocan; las tareas 4 y 5 esperan su commit. Después suma
  el par de columnas en la tabla del bloque.
- **La migración:** antes de generarla se mira la última de `main`; ninguna otra sesión tiene una pendiente conocida.
- **El usuario prueba con la app levantada:** el `src` del front se edita con su permiso (Vite le recarga la página) y se
  avisa cuando haya que reiniciar la Api.

## Compilar y probar

Con la salida propia, para no chocar con otras sesiones ni con Visual Studio:

```bash
dotnet build ArquitecturaBase.slnx --artifacts-path C:/Users/ezequ/source/repos/ArquitecturaBase/.artifacts/renglones-e2
```

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --artifacts-path C:/Users/ezequ/source/repos/ArquitecturaBase/.artifacts/renglones-e2 -- --filter-class "<Namespace.Clase>"
```

En cada corte, el build y los tests rápidos del área: `Application.UnitTests` y `ArchitectureTests` enteros antes de
commitear. La integración nueva se escribe en rojo y corre con PostgreSQL al cierre; una tarea que depende de SQL corre sólo
su clase. Otras sesiones trabajan en el mismo checkout: se preparan sólo los archivos y los fragmentos propios.

## Tareas

### Tarea 1 · Los valores con su renglón

- [x] Tests en rojo: `BusinessFormRecordValue.Create` y `BusinessFormRecordReference.Create` con `row` de 0 a 99 (fuera de
  rango lanza), `BusinessFormCaptureReceipt.Reject` con `RejectedRow`, y `FormRecordRowsMigrationTests` (Up/Down/Up con la
  historia conservada, los índices nuevos y el rechazo del Down por cada caso), como `FormMultipleChoiceMigrationTests`.
- [x] Dominio (`BusinessFormRecord.cs`, `BusinessFormRecordReference.cs`), configuraciones EF, `FormRecordReferenceRow.Row`
  y los lectores que arman referencias (`FormRecordReader` y los que leen `BusinessFormRecordReferences`).
- [x] Migración `AddFormRecordRows` según [la guía](../guides/migracion.md), con el snapshot en LF.
- [x] Commit: `feat(formularios): los valores y las referencias guardan su renglón`.

  Hecho: el recibo exige la pregunta para guardar el ítem (CHECK `CK_BusinessFormCaptureReceipts_RejectedRow`); los otros
  lectores de referencias (etiquetas de la planilla, usos de una opción, campos históricos) no necesitan el ítem. La
  migración corrió con PostgreSQL (7 tests); `FormMultipleChoiceValueModelTests` fija los índices nuevos.

### Tarea 2 · La captura con renglones

- [x] Tests en rojo en `FormRowsCaptureTests` (como `FormMultipleChoiceCaptureTests`) y `FormRecordAnswerPolicyTests`:
  guardar uno, tres y cero ítems; el `ValuesJson` exacto; un valor y una referencia por pregunta de cada ítem con su `Row`;
  el mismo artículo en dos ítems; el replay con la misma huella y el conflicto con otra; los rechazos (menos que el mínimo,
  más que el máximo, una obligatoria vacía en un ítem, una clave ajena o repetida en un ítem, una pregunta del bloque
  arriba, una opción no ofrecida, una opción Manual dada de baja con `RejectedRow`); y la huella sin bloque igual a la de
  hoy.
- [x] `FormRecordAnswerPolicy` (el `Normalize` de hoy más el bloque), `BusinessFormRecordRecorder` y
  `StructuredRecordCaptureResponse.RejectedRow`.
- [x] Las lecturas: `Project` sin el bloque ni sus preguntas, `StructuredRecordReadRow` con `Rows`, y el PDF sin el bloque,
  con sus tests (`FormRecordPresentationPolicyTests` y el del renderer).
- [x] Commit: `feat(formularios): la captura guarda los renglones del bloque`.

  Hecho: `Rows(row, culture, schema)` lee el bloque de la respuesta (uno por formulario) y da `[]` sin bloque o sin su
  respuesta; las celdas de las preguntas del bloque en la planilla pasan a «—» (NotAsked). La huella de una captura sin
  bloque quedó fijada con su valor (canal y carga desde la app). `FormRowsCaptureIntegrationTests` corrió con PostgreSQL:
  la oferta del catálogo del bloque, los ítems con su `Row` y el replay del ítem rechazado por el lector real.

### Tarea 3 · WhatsApp guarda los renglones

- [x] Tests en rojo en `WhatsAppFlowRowsCodecTests` y `WhatsAppFlowReplyServiceTests.Rows.cs`: un ítem con «No», tres con
  «Sí», «Sí» y «No», el mínimo 0 con «No» en `_r0`, un ítem que no pregunta antes del mínimo, el último sin pregunta, los
  ítems después del «No» ignorados, cada rechazo de arriba, un catálogo en el bloque con la oferta de la sesión, y la
  proyección mal formada que queda `Rejected`. La respuesta real que llegó del celular el 2026-11-07 (15 claves, `_r1` a
  `_r3`) se reproduce con valores sintéticos.
- [x] `WhatsAppFlowRowsCodec`, `ProjectionIsWellFormed` y `ProcessOneAsync` en `WhatsAppFlowReplyService`.
- [x] `WhatsAppRowsCompositionTests` suma la captura de una respuesta con ítems por el sink real.
- [x] Commit: `feat(whatsapp): la respuesta de un Flow con renglones se guarda`.

  Hecho: el mapping con la oferta de la sesión y el valor canónico pasaron a `WhatsAppFlowValueCodec` (`ForReply` y
  `Canonical`), que comparten la respuesta y el codec de ítems; el tope de 40 cuenta las preguntas sin el bloque. La
  composición corrió con PostgreSQL: publicar r10, enviar, la respuesta con tres ítems por el webhook firmado y la captura
  real con su número.

### Tarea 4 · La carga desde la app con renglones

- [x] Esperar el commit del backend de LD1-A y revisar `git status`.
- [x] Tests en rojo: `FormEntryRowsTests` (la descripción con el bloque, `Prepare`, la oferta por ítem con LD3-A, el error en
  `answers.<bloque>.<n>.<pregunta>`, la corrección con sus ítems) y la integración `FormEntryRowsHttpTests` (cargar, enviar
  tres ítems, el replay con el mismo `requestId`, los rechazos, corregir y una lista dependiente en el bloque).
- [x] `FormEntryModels`, `FormRecordAmendmentModels`, `FormEntryRequestValidators`, `FormEntryVersionPolicy`,
  `FormEntryRecorder`, `FormEntryCatalogPolicy`, `FormEntryService.GetCorrectionAsync`, y en la Api
  `FormEntryHttpRequests` y `FormEntryRequestMapping` con la lista de `FormEntryRequestMappingTests`.
- [x] Capturas para el front en la cohorte de la carga (`FORM_ENTRIES_HTTP_OUTPUT`, `forms-entries-http`, que LD1-A deja
  en 32): la carga con el bloque, el envío y la corrección con sus ítems, en `export-responses-http.ps1` y con su cuenta en
  el autotest del exportador.
- [x] Commit: `feat(formularios): la carga desde la app guarda los renglones`.

  Hecho: el backend de LD1-A entró en 78c9eca2 (un commit «avance» del usuario). Las capturas son entry-rows-es (la carga con
  el bloque), entry-rows-error-es (el 400 con la celda del ítem) y correction-rows-es (la corrección con sus ítems): la
  cohorte pasa a 35; el autotest del exportador cuenta cohortes, no cuerpos, y no cambia. No había `FormEntryRequestMappingTests`:
  el mapeo lo prueba la integración. `FormEntryRowsHttpTests` corrió con PostgreSQL.

### Tarea 5 · Publicar: el mínimo y las listas dependientes

- [x] Tests en rojo (con el backend de LD1-A ya commiteado): el problema de RG13-A (con canal, en su ruta, con `limit`; sin canal no aparece) en
  `FormGroupPublicationTests`, y LD3-A en los de `FormCatalogMatchPolicy`; en la integración, `FormGroupPublicationHttpTests`.
- [x] `FormPublicationErrors.MinRowsOverChannelLimit` con su texto en `Errors.resx` y `.en.resx`, `FormPublicationPolicy`,
  `BusinessFormPublicationService` y `FormCatalogMatchPolicy.ValidateQuestions` con sus llamadores.
- [x] Captura `rows/publication-check-minimum.json`.
- [x] Commit: `feat(formularios): publicar frena un mínimo de ítems que WhatsApp no carga`.

  Hecho: `FormPublicationPolicy.RowIssues` se suma al chequeo sólo con un canal instalado, como `RowWarnings`. El host de
  integración siempre tiene el canal de WhatsApp registrado, así que «sin canal» no se prueba por HTTP (sin canal el chequeo
  ya frena con `ChannelUnavailable`). La cohorte forms-http pasa a 70 (la guía decía 64 y no contaba las de renglones ni las
  de títulos de sección).
- [x] Revisión del backend (un revisor) y correcciones, sin cambios de bytes en lo que ya existe.

  Hecho: la revisión no encontró defectos de corrección (captura, invariancia de huella y bytes, migración, WhatsApp, carga,
  publicar, reglas del proyecto). Señaló dos de rendimiento, corregidos con su test: la oferta de la carga resolvía el catálogo
  una vez por ítem (ahora una vez por pregunta, o una por respuesta distinta de su ítem cuando la lista depende de otra pregunta
  del bloque, y el error sigue en el primer ítem que eligió la opción), y la captura revisaba la actividad de una opción Manual
  una vez por ítem bajo el lock de definiciones (ahora una vez por opción, donde aparece primero).

### Tarea 6 · El front: modelo y contrato

- [x] Pedirle al usuario permiso para editar el `src` del front y mirar `git status`.
- [x] Tests en rojo en `entryFormRows.test.ts`: el borrador inicial con el mínimo y la opción única, agregar, quitar y
  mover, la validación por ítem y de la cantidad, el envío, la versión nueva, «Mantener», la corrección, la firma y LD3-A por
  ítem; y `entriesRowsHttpContract.test.ts` sobre las capturas de la Tarea 4.
- [x] `api/entries.ts` y `entryForm.ts`.
- [x] Commit en el front: `feat(formularios): el modelo de la carga lleva los renglones`.

  Hecho (front 61235552): el guard se prueba en `entriesRows.test.ts` (bloque y preguntas repetidas, 40 más el bloque, la
  corrección con sus ítems). `entriesRowsHttpContract.test.ts` pasa a la Tarea 10: `httpCohort` verifica la procedencia de
  la cohorte entera, así que consume las capturas de la exportación conjunta; mientras, el guard leyó las tres capturas de
  `FormEntryRowsHttpTests` copiadas aparte. Los problemas de un ítem van en `entryItemPath` (`<bloque>.<n>.<pregunta>`) y
  los de la cantidad (`rowsAtLeast`, `rowsAtMost`), en el id del bloque.

### Tarea 7 · El front: la tabla y los recuadros

- [x] Abrir el tablero RG2-A del lienzo provisorio y anotar los textos que falten.
- [x] Las dos piezas de `shared/ui` con sus tests (agregar, quitar, mover, el máximo y el mínimo con su motivo, los errores
  y el foco) y su ficha en el catálogo del [estándar visual](../../../ArquitecturaBaseFront/docs/guides/estandar-visual.md).
- [x] Commit en el front: `feat(ui): la tabla de renglones y su versión en recuadros`.

  Hecho (front 5f3d50c8): `ItemTable` e `ItemCards`, con la ficha `?scenario=item-table` y la escena `itemTable` sin golden.
  El tablero no dibujaba cuatro textos, que quedaron en la tabla de textos del diseño: el motivo del tacho con el mínimo
  («Al menos 1 ítem.»), los nombres de ↑ ↓ y del tacho («Subir el ítem 2»…), el de cada celda («Ítem 2 · Cantidad») y el
  anuncio al mover («Ahora es el ítem 1 de 3.»). Quitar lleva el foco a «Agregar ítem»; las flechas son las del resto de la
  app (el chevrón), no las del tablero.

### Tarea 8 · El front: la página, el panel y el celular

- [x] `FormEntryForm`, `useEntryEditor`, `FormEntryPage`, `FormEntryPanel`, `FormEntryCorrectionPage` y `FormEntrySent`, con
  los textos es/en (`formEntries.json`).
- [x] Tests (`FormEntryRows.test.tsx`, `FormEntryPanel.rows.test.tsx`, la corrección) y la escena visual `formEntryRows`, es/en
  a 1440, 390 y 320, más el panel, sin scroll de la página.
- [x] Commit en el front: `feat(formularios): la carga desde la app con la tabla de renglones`.

  Hecho (front 9c0a7210): las páginas y el panel no cambiaron; el bloque es `EntryBlock` dentro de `FormEntryForm`, tabla
  donde la zona tiene columnas y recuadros en una columna (el panel y el celular), y la corrección lo abre con sus ítems
  en orden. El error del servidor `answers.<bloque>.<n>.<pregunta>` es la misma clave que el problema del ítem
  (`entryItemPath`); el primero en el orden del formulario (`entryProblemKeys`) se lleva el foco y el aviso lo nombra
  «Artículos · Ítem 2 · Cantidad». La escena usa dos escenarios nuevos, `entries-rows` y `responses-app-rows`.

### Tarea 9 · El front: publicar y el editor

- [x] Esperar el aviso del chat de la Etapa 1 (RG10-D y RG12-A commiteados).
- [x] El texto de RG13-A en `publicationPresentation.ts` y `forms.json` es/en, con su test y la escena del diálogo.
- [x] LD3-A en `CatalogQuestionFilters`: «· del mismo ítem», «· para todos los ítems» y «Está en el bloque», con sus tests.
- [x] Commit en el front: `feat(formularios): publicar y las listas dependientes con el bloque`.

  Hecho (front 53a57ca0, sobre RG10-D/RG12-A 06666b14): el problema de RG13-A lo arma `publicationIssueNames` con el `limit`
  del servidor y el mínimo del bloque del borrador; la escena es `forms-publication-rows-minimum` en `formRows`. LD3-A vive
  en `answerChoices` (`dependentLists.ts`): el motivo `inBlock` y de qué ítem sale la respuesta (`items`); la pregunta se
  nombra con su número de la lista (`questionNumbers`, «2.1»).

### Tarea 10 · Cierre

- [ ] Verificación: la integración con PostgreSQL y Redis, `dotnet test` completo en tandas, el build sin advertencias; en el
  front, `build`, `lint`, `test` y `test:visual` en tandas; `aspire stop` si se levantó la app.
- [ ] Exportación conjunta de la cohorte `forms-http` del front con las capturas de esta etapa.
- [ ] Prueba física, guiando al usuario clic por clic y sin cargar datos por él: pasar «Recepción de insumos» al bloque
  «Artículos» y publicarlo; cargar el remito con sus tres artículos desde la página, revisar con «Corregir» que vuelvan los
  tres en orden, y lo mismo en el panel y en el celular; por WhatsApp, los tres ítems con «Sí», «Sí» y «No», y la vuelta con
  la flecha antes del «No». Lo que llegue se mira en la base sin valores.

  El 2026-11-07 el usuario eligió hacerla dentro de la prueba integral final de las etapas de renglones, después de
  RG10-D/RG12-A (Etapa 1), la Tarea 9 y la exportación conjunta, en lugar de hacerla aparte.
- [ ] Docs: [formularios](../features/formularios.md) (los valores con su renglón, la captura y la corrección),
  [WhatsApp](../features/whatsapp.md) (la respuesta con ítems y el aviso), los `AGENTS.md` de Services, Persistence,
  Migrations, los Services de WhatsApp y las áreas del front, el diseño de la carga en el front, este plan y el de renglones.
- [ ] Ofrecer el traspaso al multitenant.
