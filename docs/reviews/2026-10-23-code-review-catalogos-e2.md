# Code review: Catálogos E2 — 2026-10-23

Revisión de solo lectura de backend `66900db2..dab9c281` y front `9f0d270..e02d1b4`. Se contrastó con:
- el [diseño histórico de catálogos](../specs/2026-10-21-catalogos-design.md);
- el brief retirado `catalogos-campos-extra.md` (en `dab9c281^`);
- los tableros CE1, CE2, CE3-B, CE4-A, CE5 y CE6.

Cubre E2-0 a E2-4: campos extra, datos de las personas, filtros por destinatario, qué viaja a WhatsApp y las respuestas. Los hallazgos graves se verificaron leyendo el código.

**Lo que está bien:**
- **Huellas:**
  - todo lo nuevo que se sella lleva `JsonIgnore(WhenWritingNull)`: `Match`, `Presentation`, `Shown`, `Fields` y `FieldValuesJson`;
  - un test fija el hash literal de un esquema anterior a E2;
  - el canal recibe solo `Source?.Catalog`;
  - `Normalize` ordena `Match` y convierte `[]` en null.
- **Filtros:**
  - el SQL es parametrizado y la contención se aplica antes del tope de 201;
  - sin el dato, el envío se frena con `RecipientDataMissing`;
  - con un campo o un dato inactivo, se frena con `CatalogFilterUnavailable`;
  - ningún camino quita el filtro ni manda la lista entera;
  - asistente, prellenado, aclaración y sugerencias usan la misma preparación.
- **A Meta** viajan solo `id`, `title` y `description` desde `Shown`. Un test de integración comprueba que no viajan el texto, el detalle ni un campo privado.
- **Guardado y permisos:**
  - una transacción por método público que escribe;
  - locks ordenados y sin ciclos;
  - permisos revalidados dentro del límite, como fija la §10;
  - rutas en el inventario.
- **Datos de las personas:**
  - `ToString()` redactado y logs solo con IDs;
  - la tabla no devuelve el correo;
  - las personas sin el dato salen de un solo SQL por lote;
  - el `Down` de la migración se niega si hay filas.
- **El catálogo:**
  - E2-0 está completo: compara por `Source?.Catalog`, hay un solo mapeo y el uso cuenta el borrador y la versión activa;
  - la identidad y la clave de cada campo son estables;
  - un campo nuevo nace opcional;
  - pasar a obligatorio y cambiar el tipo devuelven conteos y filas;
  - quitar un campo en uso da 409, y con datos pide confirmación;
  - la fuente es Manual, activa y distinta del propio catálogo, con `SourceInUse` bajo lock.
- **Formatos e importación:**
  - el formato canónico se guarda por tipo;
  - importar y pegar usan la cultura y `ValueAmbiguous`;
  - los encabezados se reconocen por clave, por nombre, `[campo]` y `[canónico]`;
  - una columna ausente conserva su valor.
- **Captura:** sella solo los campos activos con valor, y un test edita el catálogo después de capturar.
- **Migraciones:**
  - `Up` aditivo, CHECK de una línea e índice GIN;
  - `Down` con lock y `RAISE`;
  - snapshot coherente.
- **Los dos pendientes del [review E1](2026-10-22-code-review-catalogos-e1.md) están resueltos:**
  - la columna obligatoria faltante o repetida se muestra en el diálogo;
  - «Aplicar» sin diferencias conserva la revisión.
- **Front:**
  - `WideTable` accesible;
  - rutas de validación por segmentos;
  - la configuración de la pregunta de catálogo, separada;
  - decimales exactos en es/en;
  - zod estricto;
  - los datos de las personas, sin MutationCache, con `gcTime 0` y `no-store`, enviados como delta y con la sección opcional sin nombrar el módulo;
  - publicar separa problemas y avisos;
  - las subcolumnas de CE5;
  - los motivos en el chat;
  - i18n en paridad y con voseo.

Se corrige en un solo chat, back y front juntos, cada arreglo con su test y commits chicos. Las decisiones no cambian. Después va el [piloto único con Meta](../plans/verificacion-y-produccion.md#formularios-piloto-único-con-meta-después-de-e2).

## Arreglar ahora

**Alta: dejan el catálogo o su editor sin poder usarse**

1. **Dar de baja una opción de un catálogo fuente bloquea la edición y la importación de los catálogos que la usan** (`FormCatalogFieldPolicy.Items.cs:45-58`, `FormCatalogFieldSourceIndex.cs:15-24`, llamado desde `FormCatalogItemRecorder.cs:110-115`).
   - **Qué pasa:**
     - `ValidateItems` revalida en cada guardado todos los valores guardados, también los de opciones inactivas, contra las opciones **activas** de la fuente;
     - si en «Zonas» se da de baja «Norte» y 300 proveedores tienen Zona = Norte, desde ahí cualquier PUT de «Proveedores» responde 400 `FieldValueInvalid` en `fields[i].type`, sin decir qué falla;
     - lo mismo pasa al renombrar, al editar un CUIT o al agregar una opción;
     - importar pasa la vista previa y falla al aplicar, y exportar y reimportar sin tocar nada también falla.
   - **Arreglo:**
     - exigir una opción activa solo para valores nuevos o cambiados y para las conversiones;
     - aceptar los valores guardados que apuntan a una opción inactiva, como ya hace el delta de los datos de las personas;
     - importar también toma los locks de los catálogos fuente, ordenados, como el PUT (`FormCatalogImportService.cs:66,101-115`).
2. **Un 409 de una regla de negocio obliga a descartar todo el borrador del catálogo** (front: `useCatalogDraftEditor.ts:129-130`, `CatalogEditorPage.tsx:103,183,195-197`, `CatalogFieldsSection.tsx:47` y `catalogFieldFeedback.ts:4-20`).
   - **Qué pasa:**
     - cualquier 409 pasa el editor a revisión de solo lectura, y la única salida es «Usar la revisión actual», que pierde lo editado;
     - le pasa a `SourceInUse`: en «Zonas», editar opciones y dar una de baja;
     - y a `FieldInUse`: sumar CUIT a «En WhatsApp» y después quitar el campo CUIT;
     - «Restaurar el campo» no aparece y se ignora `uses`.
   - **Arreglo:**
     - entrar en revisión solo con `Forms.Catalog.RevisionConflict`;
     - los otros 409 son errores que se corrigen en el editor, mostrando `uses`;
     - al quitar un campo, sacarlo también de la presentación del borrador.
3. **Un nombre en inglés vacío deja el catálogo sin poder abrirse** (front: `CatalogFieldEditor.tsx:25`, `catalogFields.ts:58` y el guard de `catalogResponse.ts:16`; back: `FormCatalogFieldRecorder.cs:40` y `BusinessFormCatalog.Fields.cs:38`).
   - **Qué pasa:**
     - si alguien escribe el nombre en inglés de un campo y lo borra, viaja `en: ""` y el back lo guarda;
     - desde ahí falla el guard del PUT y el de cada GET: el guardado se ve incierto, «Releer» falla y el catálogo no vuelve a abrir;
     - además, una columna sin encabezado se toma como ese campo al importar (`FormCatalogImportPolicy.Fields.cs:25-26`).
   - **Arreglo:**
     - el front omite el inglés en blanco, como ya hace `PersonAttributeDialog`;
     - el back descarta las traducciones vacías al guardar, como en los ítems;
     - la importación ignora los encabezados vacíos;
     - no se relaja el guard.
4. **Cambiar el tipo o pasar a obligatorio se valida en el front y tapa la revisión del servidor** (front: `CatalogEditorPage.tsx:133-139,162,181`, `catalogDraft.ts:36,45-52`, `catalogFields.ts:33` y `CatalogFieldEditor.tsx:27,29`).
   - **Qué pasa:**
     - pasar «Zona» de texto a «Elegir de un catálogo» marca en error cada fila visitada y deshabilita Guardar, así que el back, que sí convierte por código o texto, nunca recibe el pedido;
     - pasar CUIT a obligatorio con faltantes deshabilita Guardar, y nunca aparece el «faltan N» del servidor;
     - cambiar de entero a texto y editar una fila en el mismo guardado manda formatos mezclados.
   - **Arreglo:**
     - validar las filas no editadas contra la definición guardada y mandar solo los valores editados;
     - que el servidor responda el cambio de tipo y el obligatorio;
     - sumar el diálogo «Cambiar el tipo» de CE1, con la fila, el valor y el motivo, «y N más» y «Ver los N en la tabla», a partir de `invalidRows`.

**Media: guardan datos mal o dejan un formulario sin poder mandarse**

5. **Pasar un campo de texto a decimal interpreta «1.500» como 1,5** (`FormCatalogFieldPolicy.Items.cs:44` → `FormCatalogFieldPolicy.cs:25-31` → `FormCatalogValuePolicy.cs:23-28`).
   - **Qué pasa:** la conversión usa la lectura invariante. Guarda «1.5» y «12.345» sin avisar, mientras que la importación da `ValueAmbiguous`. Es el mismo error de cultura de E1, en otro camino.
   - **Arreglo:** convertir con `NormalizeImport` en la cultura del pedido; un valor ambiguo no convierte.
6. **Pasar un campo de catálogo a texto copia el GUID de la opción** (`FormCatalogFieldPolicy.Items.cs:39-44`, `FormCatalogFieldPolicy.cs:18-21`).
   - **Qué pasa:**
     - el GUID queda en la tabla y en el Excel, y también en `Shown`: si el campo está en la descripción, viaja a Meta, y si está en el título, pasa los 30 caracteres y frena los envíos;
     - de booleano a texto quedan «true» y «false».
   - **Arreglo:** convertir con el texto visible: el de la opción fuente, y «Sí» o «No» en el idioma original del catálogo.
7. **Cambiar el tipo o la fuente de un dato de las personas deja valores huérfanos** (`FormPersonDataService.cs:117-121`, `BusinessFormPersonAttribute.cs:27-35`; el efecto, en `FormCatalogMatchPolicy.cs:56-60` y `FormPersonDataReader.RecipientStatistics.cs:37-39`).
   - **Qué pasa:**
     - el control de «en uso» solo frena si un filtro ya usa el dato, y el cambio no toca `ValuesJson`;
     - si se corrige la fuente de «Zona» de «Zonas» a «Regiones», las 300 personas quedan con IDs de «Zonas»;
     - el aviso de publicar dice que nadie está sin el dato, la contención da 0 opciones y sale `CatalogEmpty` en lugar de `RecipientDataMissing`, o se manda una lista vacía;
     - pasar de texto a catálogo bloquea a todos con `CatalogFilterUnavailable`, y de catálogo a texto muestra GUID.
   - **Arreglo:**
     - si alguien tiene el dato cargado, cambiar el tipo o la fuente pide confirmación y limpia esos valores, como quitar un campo con datos;
     - la preparación trata como `RecipientDataMissing` un valor que no es una opción de la fuente.
8. **Cambiar el papel, el tipo o la fuente de un campo que usa un filtro no se frena** (`FormCatalogFieldRecorder.cs:50-73`; el efecto, en `FormCatalogMatchPolicy.cs:168-175` y `FormCatalogOptionsPolicy.cs:39-44`).
   - **Qué pasa:** pasar «Zona» de filtro a dato, por ejemplo para verla en la planilla, se guarda sin aviso, y desde ahí todos los envíos del formulario publicado dan `CatalogFilterUnavailable`. Para los datos de las personas sí se frena.
   - **Arreglo:** cualquier cambio de `Role`, `Type` o `SourceCatalogId` de un campo que usa un `match` del borrador o de la versión activa da `FieldInUse` con `uses`. Se puede reutilizar `FindFieldUsageAsync`. Ajustar el test que hoy fija la falla como esperada.
9. **La vista previa no detecta repetidos de un campo único con «ñ», tildes, «+» o «&»** (`FormCatalogImportPolicy.Fields.cs:82-85,116-118`).
   - **Qué pasa:** compara `GetRawText()`. El valor guardado viene de jsonb sin escapar y el normalizado viene escapado. «Peñalba S.A.» repetida pasa la vista previa y al aplicar da `FieldValueDuplicate`, que el diálogo muestra como aviso genérico.
   - **Arreglo:** comparar el valor decodificado, con un test que lea los datos de PostgreSQL.
10. **Cada guardado reescribe las 10.000 opciones y su auditoría** (`FormCatalogFieldPolicy.Items.cs:76-80`).
    - **Qué pasa:**
      - asigna el JSON a todos los ítems, y como PostgreSQL devuelve jsonb con espacios, EF los marca modificados;
      - renombrar el catálogo hace 10.000 UPDATE, y todas las opciones figuran modificadas por esa persona;
      - «Aplicar» sin diferencias también reescribe;
      - mientras tanto se retiene el lock del catálogo, que también usa el envío.
    - **Arreglo:** llamar a `SetManualItemFields` solo si el mapa normalizado cambió, con un test de que un guardado sin cambios no toca `ModifiedAtUtc`.
11. **«En uso» muestra nombres de formularios a quien no puede leerlos** (`FormCatalogFieldPresentation.cs:26-36`, `FormCatalogFieldRecorder.cs:62-68`, `ProblemDetailsMapper.cs:117-126`).
    - **Qué pasa:** con solo `formcatalogs.read` se ven `usedBy` y los `uses` del 409, con nombres e IDs de formularios y borradores. Contradice que `UsageCount` sea null sin `formtemplates.read`.
    - **Arreglo:** dar los nombres solo con `formtemplates.read`; sin ese permiso, solo el bloqueo y la cantidad.
12. **Importar o pegar después de guardar campos usa la definición vieja** (front: `CatalogEditorPage.tsx:54-59,193`, `CatalogImportDialog.tsx:32-41`).
    - **Qué pasa:** el diálogo recibe el detalle de cuando se entró. «Altas» no muestra los campos nuevos, y «Cambios» muestra GUID como nombre de columna y «GUID → GUID» en Zona.
    - **Arreglo:** pasar el catálogo guardado (`editor.saved?.catalog ?? initial`).
13. **Vaciar una celda de catálogo sigue mostrando la opción anterior** (front: `CatalogExtraFieldInput.tsx:28,30`, `CatalogItemsTable.tsx:61`, `CatalogItemFields.tsx:34`).
    - **Qué pasa:** el valor queda en null, pero la celda vuelve a mostrar «Centro». Al guardar se borra sin que se vea.
    - **Arreglo:** mostrar el texto solo si corresponde al valor actual.
14. **El valor de un filtro fijo sobre un campo de catálogo no se ve** (front: `CatalogQuestionFilters.tsx:15-26`, `CatalogExtraFieldInput.tsx:26,28,41`, `CatalogQuestionSourceSummary.tsx:22`).
    - **Qué pasa:** la etiqueta se busca entre las primeras 20 opciones. «Zona es Sur» en un catálogo de 72 se ve vacía al reabrir, en solo lectura y en la revisión de un conflicto.
    - **Arreglo:** resolver la etiqueta por ID.
15. **Los filtros y «Propio de esta pregunta» se ofrecen en catálogos de Filtros** (front: `CatalogQuestionSettings.tsx:48-54`; back: `FormPublicationGuard.cs:54-57`).
    - **Qué pasa:** se guarda bien, pero publicar se frena con `MatchInvalid` aunque no haya filtros.
    - **Arreglo:** mostrar esas secciones solo con catálogos Manual.
16. **En la pregunta, el aviso de títulos de más de 30 caracteres mira solo 20 opciones** (front: `CatalogQuestionSettings.tsx:31,54`, `CatalogPresentationSection.tsx:27,34-35`).
    - **Qué pasa:** con 41 títulos largos entre 1.200 opciones, puede decir «1 opción» o nada. CE3-B muestra «41 opciones… y 38 más» con ejemplos.
    - **Arreglo:** que el servidor calcule el chequeo para la fuente de la pregunta, en es y en en, como ya lo hace para el catálogo.
17. **El desplegable de una celda de catálogo se recorta dentro de la tabla ancha** (front: `shared/ui/Autocomplete.tsx:28`, `shared/ui/table.tsx:8`). Es plausible, sale del CSS.
    - **Qué pasa:** en las últimas filas, o con pocas filas, la lista se corta o obliga a desplazar la caja de la tabla.
    - **Arreglo:** mostrar la lista en un portal, y verificarlo en el navegador.

**Baja: arreglos chicos**

18. **Un filtro fijo que deja más de 20 opciones en «elegir varias» ya no frena publicar** (`FormPublicationGuard.cs:95-107`). Antes de E2 frenaba con `CatalogTooLargeForMultiple`; ahora queda como aviso y fallan todos los envíos. Si no hay filtros por persona, el máximo calculado es un problema y no un aviso.
19. **El valor fijo de un filtro sobre un campo de catálogo no se valida contra su fuente** (`FormCatalogMatchPolicy.cs:38`, `FormCatalogFieldPolicy.cs:38-41`). Un ID inexistente, ajeno o inactivo da 0 opciones para todos. La comprobación verifica que la opción exista y esté activa.
20. **«Como si lo recibiera» no exige `formcatalogs.read`** (`FormRecipientPreviewsController.cs:17-18,29-31`, `FormRecipientPreviewService.cs:27-61`). Quien solo administra formularios puede armar una presentación sobre cualquier catálogo y ver hasta 200 valores. La UI ya lo necesita para elegir catálogo y campos.
21. **Las sugerencias pasan los campos sellados al contrato del núcleo** (`FormSuggestionCandidatePolicy.cs:27,55`). Se pasa `includeCatalogFields: false`, como en el chat. Hoy no se filtra nada, pero contradice la regla del puerto.
22. **Cambiar reglas o reactivar un campo con valores incompatibles da solo el primer error, en el control equivocado** (`FormCatalogFieldPolicy.Items.cs:21,49-66`, `FormCatalogFieldRecorder.cs:41-46`, `BusinessFormCatalog.Fields.cs:46-50`). Afecta a bajar `maxLength`, cambiar `min` o `max` y activar «único». Hay que acumular como `TypeChanges`, y validar esos cambios también en un campo inactivo.
23. **El chequeo de presentación del catálogo mira solo la cultura de la UI y no las descripciones** (`FormCatalogFieldPresentation.cs:22,40-44`). Se calcula en es y en en, con el tope de 300 de la descripción.
24. **Nombres según el [ADR 0008](../decisions/0008-nombres-de-repositorios-y-lectores.md):** `IFormRecordRepository.FindCatalogFieldValuesAsync` e `IFormCatalogReader.FindFieldUsageAsync` devuelven colecciones, así que van como `List…`. Que `PersistenceNamingTests` lo detecte.
25. **«En uso» no dice si es el borrador o la versión activa** (front: `CatalogFieldsSection.tsx:55`, `catalogResponse.ts:12`). Se ignora `stage`, el uso por el propio «En WhatsApp» aparece como un formulario y falta «Ir a la pregunta» de CE1.
26. **Datos de las personas** (front: `UserDataSectionShell.tsx:14-19,31`, `PersonAttributeDialog.tsx:33-38`, `PersonDataPage.tsx:40,57`):
    - al abrir una persona, la carga y el error se dibujan fuera de la pantalla, y con un 403 no se ve nada;
    - la tabla no se actualiza al cambiar el nombre;
    - un 409 al editar un dato dice «Volvé a cargarlo», pero no hay cómo hacerlo.
27. **Guardar no hace nada, sin aviso, si se editó una opción inactiva a la que le falta un obligatorio** (front: `useCatalogDraftEditor.ts:97` contra `CatalogEditorPage.tsx:133,181`). Se aplica `required && isActive` en los dos lugares.
28. **«Como si lo recibiera» no se puede reintentar y no espera ante un 429** (front: `useRecipientPreview.ts:26-42`).
29. **Guards más laxos que el contrato y un plural mal** (front: `api/response.ts:15,118`, `catalogResponse.ts:43`, `locales/es/forms.json:247-248`):
    - los avisos de publicar admiten cualquier `code` y detalles opcionales;
    - se ve «Como mucho 1 opciones».
    - El arreglo es una unión discriminada por `code` y claves `_one` y `_other`.
30. **Con campos quitados ocultos, las flechas a veces no mueven nada a la vista** (front: `CatalogFieldsSection.tsx:48-49`). Hay que mover solo entre los activos.
31. **El formato de cada tipo no sale de una sola pieza**, como pedía E2-0 (front: `catalogPresentation.ts:19`, `responses.ts:105`, `CatalogImportDialog.tsx:35-41`, `CatalogQuestionSourceSummary.tsx:22`, `CatalogItemsTable.tsx:61`). La misma fecha se ve «15/03/2027» en la tabla y «15 mar 2027» en la revisión de la importación.
32. **Lo que falta de los tableros:**
    - **CE1:** la columna «#», el detalle del tipo («Zonas · 72 opciones», «De 0 a 180») y «y sus N valores» en la casilla de quitar;
    - **CE2:**
      - el error de la fila con el valor y el motivo («Fila 3 · Plazo: «30 días» no es un número entero»);
      - la indicación de que hay más columnas a la derecha;
      - las ayudas de tipo y rango en el celular;
    - **CE3-B:** qué campos usa «Como en el catálogo»;
    - **CE4-A:**
      - en el editor de la pregunta, «Como mucho N por persona» y «Todos ven las mismas N»;
      - en la tabla de personas, «N personas · M sin zona»;
      - el bloqueo en el chat nombra a la persona y el dato que le falta;
    - **CE5:** «Columnas · N ocultas».
33. **Pruebas que faltan:**
    - con PostgreSQL:
      - quitar un campo en uso en el borrador y en la versión activa;
      - `PersonAttribute.InUse` con borrador y con versión activa;
      - el asistente con una persona sin el dato, que la §13 pedía con Docker;
    - `FieldInUse` y los usos `Draft` y `Active` en las cohortes exportadas, consumidos por el front, como pedía la §6.2;
    - el formato canónico con cultura de los campos tipados al importar, por HTTP.

**Extra: lo que apareció al usar la app**

34. **El chat de Inicio no muestra la respuesta** (front: `api/assistant.ts`, `assistant/model.ts`, `AiAssistantPage.tsx` y `AssistantOperationState.tsx`; back: `AiAssistantResponsePolicy.State`).
    - **Qué pasa:**
      - el back devuelve el estado en minúsculas («pending», «completed»), y sus tests de integración lo fijan así;
      - el front lo espera con mayúscula inicial;
      - al mandar «Hola», el front da la consulta por terminada, deja de consultar, muestra «states.pending» sin traducir y nunca muestra la respuesta;
      - pasa desde el 2026-10-09 por lo menos, y no hay ejemplos exportados del asistente que lo detecten.
    - **Arreglo:**
      - el contrato publicado es el del back: corregir en el front los tipos, las comparaciones, las claves es/en y los dobles de los tests;
      - exportar ejemplos HTTP reales del asistente (bootstrap, enviar, turno, turnos y cancelar) y validarlos con zod estricto.
35. **Nadie está obligado a escribir en inglés ni en español** (back: `FormCatalogRequestValidators.cs:183-184` y `BusinessFormPersonAttribute.cs`; front: `CatalogMetadataFields.tsx:25,30` y `catalogResponse.ts`).
    - **Qué pasa:**
      - el alta de catálogos sigue siendo bilingüe: el nombre y la descripción exigen es y en. Es la excepción que dejó la [regla de idioma original](../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19) del 2026-10-19;
      - los nombres de los campos, el texto de las opciones y los datos de las personas exigen español;
      - al crear un catálogo solo en español, «Guardar» parece no hacer nada: el 400 trae `name.en` y `description.en`, y el front lo muestra solo en la pestaña del idioma activo;
      - la persona ve que no pasa nada y, al irse, pierde lo escrito.
    - **Regla, decisión del usuario del 2026-10-23:**
      - lo que se crea en la web se escribe en el idioma elegido en el menú; ese es su idioma original y el único obligatorio;
      - el otro idioma es una traducción opcional;
      - se muestra en el idioma de la pantalla y, si falta, en el original;
      - **no hay selector de idioma en las pantallas.** Para traducir, se cambia el idioma del menú y se edita lo mismo:
        - se saca «Textos en» de `FormEditorPage`, `CatalogMetadataFields`, `OfficialFormPreview` y `FormPdfPreview`;
        - los pares de campos por idioma, como «Nombre» y «Nombre en inglés» en `CatalogFieldEditor` o `PersonAttributeDialog`, pasan a un solo campo en el idioma del menú;
        - con el menú en el idioma que no es el original, un campo sin traducción muestra el original como referencia, sin copiarlo;
        - la comprobación de publicación sigue avisando lo que falte del original;
        - esto cambia el editor de formularios de la etapa de idiomas (I1–I3): se anota en las reglas y en el diseño del front.
    - **Arreglo:**
      - es la misma regla que ya tienen los formularios: se extiende a los catálogos (nombre, descripción y opciones), a los campos y a los datos de las personas, y se retira la excepción bilingüe;
      - hay que revisar todo lo que hoy exige «es», «es y en» o cae a «es» por defecto, también lo que viaja a WhatsApp y las pantallas que dicen «Completá … en español e inglés»;
      - se anota en las reglas de formularios;
      - un error de guardado en un campo que no está a la vista tiene que verse en la página. Como no hay pestañas de idioma, un error del idioma original con el menú en el otro idioma dice qué falta y en qué idioma.
36. **Al agregar una opción, sus campos aparecen en rojo antes de escribir nada** (front: `CatalogEditorPage.tsx`, con `catalogItemProblems`, y `CatalogManualItemsEditor`).
    - **Qué pasa:** «Agregar opción» crea la fila vacía y enseguida marca Texto y Código con «Completá este campo». Arriba aparece «Revisá las opciones marcadas antes de guardar».
    - **Arreglo:**
      - un campo vacío se marca recién al intentar guardar, o al salir de un campo que la persona ya tocó, nunca al agregar la opción;
      - los otros errores, como el código repetido o el valor inválido, siguen apareciendo al escribir.
37. **Los botones de «Opciones» quedan desalineados por el aviso «Guardá el catálogo antes»** (front: `CatalogOptionsActions.tsx`).
    - **Qué pasa:** el texto debajo de los botones los empuja, y quedan desalineados con el título y con el margen de la sección. Pasa lo mismo con «Primero guardá los cambios».
    - **Arreglo, decisión del usuario del 2026-10-23:**
      - se saca ese texto visible;
      - «Agregar opción», «Pegar desde Excel», «Importar», «Exportar» y el menú van alineados al margen derecho de la sección, a la altura del título;
      - el motivo de un botón deshabilitado queda como ayuda al pasar el mouse o con el foco, y para el lector de pantalla;
      - si el tablero mostraba el texto, se anota el cambio en el diseño del front.
38. **Botones y estados en todo el front.** Es una decisión del usuario del 2026-10-23, y quedó como regla en `visual-baseline.md` del front, en «Pantalla» y en «Formularios y diálogos».
    - **Qué pasa:**
      - hay textos que explican los botones y cambian con cada acción;
      - aparece el estado «Cambios sin guardar» y hay números de revisión al lado de Guardar;
      - los botones quedan corridos hacia el medio del encabezado;
      - hay errores que aparecen antes de intentar guardar, aunque la regla ya decía «se valida al enviar».
    - **Lugares conocidos** (no es la lista completa, hay que revisar todo el front):
      - **textos al lado de los botones:**
        - `FormRulesSection` y `FormSuggestionsSection`: «No hay cambios para guardar», «Revisá los datos marcados antes de guardar», la espera y el conflicto;
        - `CatalogEditorPage`: «Revisá las opciones marcadas antes de guardar»;
        - `CatalogOptionsActions` (punto 37);
        - `CatalogQuestionSettings`: «Guardá el formulario para probar con una persona»;
      - **«Cambios sin guardar»:** `FormRulesSection`, `FormSuggestionsSection`, `CatalogEditorPage`, `CatalogFiltersEditorPage`, `FormPdfDesignPage`, `AiSettingsPage`, `AppearancePage`, `SettingsPage`, `RoleEditorPage` y `BusinessInformationPage`;
      - **números de revisión al lado de Guardar o en los encabezados:**
        - en `FormRulesSection`, `FormSuggestionsSection` y `FormPdfDesignPage`;
        - el «Revisión N:» del resumen de `AiSettingsPage`;
        - en los historiales de revisiones se conservan;
      - **errores antes de tiempo:** al agregar una opción (punto 36) y al agregar una acción a una regla («Completá este dato»);
      - **otros lugares que encontró el relevamiento del front:** la insignia de estado del editor del formulario, el compositor del chat y las acciones de D2.
    - **Arreglo:** aplicar en todas las pantallas las reglas de `visual-baseline.md`:
      - un botón está habilitado o deshabilitado, sin textos al lado, y no cambia de lugar ni de forma con cada acción. Si el motivo no es obvio, va como tooltip;
      - no hay «Cambios sin guardar» ni números de revisión en los encabezados ni al lado de los botones. Se conserva el aviso al salir con cambios sin guardar;
      - las acciones de cada superficie van al borde derecho de su encabezado, a la altura del título;
      - se valida al intentar guardar; después del primer error, en vivo mientras se corrige;
      - se actualizan las pruebas visuales y la evidencia.
39. **La sección «El catálogo»** (front: `CatalogMetadataFields.tsx`; back: `FormCatalogRequestValidators.cs:184`). Es una decisión del usuario del 2026-10-23:
    - la sección tiene solo Nombre y Descripción, y la descripción es opcional, en el back y en el front;
    - «De dónde salen» y «Tipo de valor» se eligen en el paso anterior y no se cambian después. Se muestran como dato de solo lectura, sin caja ni explicación, por ejemplo «Escritas a mano · Texto» en una línea arriba de Nombre;
    - se sacan los textos que explicaban que no se pueden cambiar.

**Extra: encontrado al relevar el back para el traspaso al multitenant**

40. **La captura responde 500 si el valor de un campo «elegir de un catálogo» apunta a una opción que no es de la fuente actual del campo.** Es un valor sellado de antes de un cambio de fuente.
    - **Arreglo:** que no sea una excepción. Si después de los puntos 1, 6, 8 y 22 ese estado ya no puede darse, alcanza con la regresión que lo demuestre; si puede darse, se sella sin el texto de la fuente o se rechaza con un motivo estable.

**Extra: encontrado al relevar el front para el traspaso al multitenant**

41. **Cambiar el idioma del menú pierde el borrador sin preguntar** en los editores del formulario y del catálogo.
    - **Qué pasa:** con la regla del punto 35, traducir es cambiar el idioma del menú y editar lo mismo. Hoy, ese gesto descarta lo que no se guardó, sin aviso.
    - **Arreglo:**
      - cambiar el idioma con cambios sin guardar pasa por el mismo aviso que salir de la pantalla;
      - mejor todavía, el borrador se conserva al cambiar de idioma, porque es el mismo objeto;
      - con su prueba.
42. **Contratos que quedaron desalineados mientras se corrige,** para el export conjunto del cierre:
    - la captura `fields-conversion-error.json` no trae las filas que exige el guard del front;
    - el guard estricto de los usos todavía no acepta el `questionId` que suma el back en el punto 25;
    - el front no manda `confirmValueRemoval`, que el back pide desde el punto 7.

## Después, sin bloquear

- **Rendimiento:** medir antes de optimizar.
  - la preparación carga todas las opciones de cada fuente de la presentación para ofrecer 201 (`FormCatalogOptionsPolicy.cs:57-58`);
  - el asistente relee los datos de la persona en cada formulario;
  - la aceptación recompone títulos bajo el lock global de definiciones;
  - la planilla expande en cada pedido los campos sellados (`FormResponseReader.CatalogFields.cs:27-38`);
  - el detalle del catálogo carga las 10.000 opciones para calcular `hasValues`;
  - la importación hace `Guid.ToString()` por cada fila, campo y campo.
- **«En uso» no se serializa con el guardado del borrador ni con la aceptación** (`FormCatalogFieldRecorder.cs:50-51`). Quitar un campo mientras se acepta una publicación que lo filtra deja los envíos fallando, pero cerrados. Hay que revalidar en la aceptación o dejarlo aceptado según el [ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md).
- **Sin `formcatalogs.read`, quien diseña formularios no ve los filtros guardados de una pregunta.** Es coherente con el punto 20; si hace falta, se suma un resumen de solo lectura.
- **La búsqueda de personas queda en la URL** (`?search=…`): alinearla con la lista de usuarios.
- **Con una fila expandida, las celdas fijas de la tabla ancha se transparentan** al desplazar de costado. Es plausible.
- **Despliegue:** volver a una build anterior a E2 con versiones publicadas que tienen filtros mandaría las listas sin filtrar, porque el código viejo ignora `match`. Hay que anotarlo en la guía de despliegue. Además, el `Down` de `20261023021502` no mira los filtros dentro de los formularios.
- **El `Down` de `20261017233619_AddFormAssistantSuggestionsAndAudio`** protege solo los datos de WhatsApp. Borra sin guarda las sugerencias, los pedidos de formularios que faltan, la procedencia de las respuestas y las propuestas de catálogo.
- **En el PUT de un catálogo, `presentation` no es un delta:** si el pedido no la trae, vuelve a la predeterminada. El front siempre la manda, así que hoy no pasa; hay que confirmar que ningún otro llamador la omita.
- **Documentación y código no coinciden en el tope de la oferta:** la documentación dice 201 para elegir una y 21 para elegir varias, pero el código usa un solo `Take(201)`. Hay que alinearlos.
- **Quitar el módulo** se verificó en una copia aislada y no quedó automatizado. Sigue pendiente la guía `quitar-formularios.md`.
- **El replay de una sesión sellada antes de E2** no tiene una prueba de punta a punta por el recorder nuevo. Por construcción, el riesgo es bajo.

## Para el piloto

- **Una pregunta opcional de «elegir varias»** filtrada que queda sin opciones para una persona manda un `CheckboxGroup` vacío. La referencia de Meta pide de 1 a 20; E1 solo confirmó el Dropdown vacío. Hay que confirmar qué hace Meta.

## Cierre local — 2026-10-26

Se corrigieron los 42 puntos, back y front juntos, del 23 al 26 de octubre. Antes de cada arreglo se contrastó el punto con el código, y cada uno tiene su regresión, vista en rojo antes de corregir. Al terminar, tres verificaciones de solo lectura dejaron hallazgos menores, agrupados en seis lotes FX ([verificación final](2026-10-25-verificacion-final-e2/README.md)), que también se corrigieron.

Se reprodujeron los 42 puntos. Los que se cumplen en parte o se resolvieron distinto de lo previsto:
- **22:** cada guardado informa todos los valores incompatibles del primer control afectado; los de otro control salen en el guardado siguiente.
- **35:** `FormPdfPreview` no tenía selector «Textos en»: estaba en `FormPdfDesignPage` y se quitó ahí.
- **37:** debajo de «Opciones» no había un «Primero guardá los cambios» literal, sino su equivalente, que también se quitó.
- **39:** las ayudas del paso anterior, donde se eligen el origen y el tipo, se conservan.
- **42:** el código ya estaba alineado; faltaba regenerar la captura.

| Punto | Estado y evidencia |
| --- | --- |
| 1 | Corregido. Un valor guardado que apunta a una opción dada de baja se acepta, y la opción activa se exige solo a lo nuevo, cambiado o convertido; el guardado que no es opción de la fuente se rechaza. Reimportar sin cambios no es un cambio, e importar toma los locks de las fuentes ordenados, como el PUT. Regresiones unitarias y HTTP. |
| 2 | Corregido. Solo `Forms.Catalog.RevisionConflict` pasa el editor a revisión. Los demás 409 se corrigen en el editor y muestran sus usos, y quitar un campo lo saca de la presentación del borrador (restaurarlo lo devuelve). |
| 3 | Corregido. El front no manda traducciones en blanco, el back las descarta al guardar y la importación ignora los encabezados vacíos. El guard no se relajó. |
| 4 | Corregido. Lo no editado se valida contra la definición guardada, viaja solo lo editado (delta) y el servidor responde el cambio de tipo y el obligatorio. El diálogo «Cambiar el tipo» de CE1 sale de `invalidRows`, con fila, valor visible y motivo, «y N más» y «Ver los N en la tabla» (opciones por `ids`). |
| 5 | Corregido. Pasar texto a número o fecha usa `NormalizeImport` en la cultura del pedido, y lo ambiguo no convierte. Regresión también por el recorder. |
| 6 | Corregido. Catálogo o sí/no a texto guardan el texto visible en el idioma original del catálogo. Una opción que no convierte a otro tipo o fuente se informa por su texto, no por su GUID. |
| 7 | Corregido. Cambiar el tipo o la fuente de un dato con valores pide confirmación (`ValueRemovalNotConfirmed` con `count`) y limpia esos valores. Un valor que no es opción de la fuente cuenta como faltante en el envío, en la tabla y en el aviso de publicar. |
| 8 | Corregido. Cambiar papel, tipo o fuente de un campo que filtra el borrador o la versión activa da `FieldInUse` con sus usos. El test que fijaba la falla se ajustó, y la captura `preparation-filter-unavailable` se retiró porque ya no se produce por HTTP. |
| 9 | Corregido. La vista previa compara los únicos por su valor decodificado. Regresión con datos leídos de PostgreSQL. |
| 10 | Corregido. Solo se escribe la opción cuyo mapa cambió (`JsonElement.DeepEquals`). Un guardado sin cambios no toca `ModifiedAtUtc`. |
| 11 | Corregido. `usedBy` y los `uses` del 409 nombran formularios solo con `formtemplates.read`; sin él, uno por formulario y etapa, sin nombres ni identidades. Regresión por HTTP. |
| 12 | Corregido. Importar y pegar usan el catálogo guardado más reciente. |
| 13 | Corregido. Una celda de catálogo vacía no muestra la opción anterior. |
| 14 | Corregido. La etiqueta de un valor fijo se resuelve por id (`GET items?ids=`), también en solo lectura y en la revisión de un conflicto, con «…» mientras carga y «Valor no disponible» si no es una opción. |
| 15 | Corregido. Los filtros y «Propio de esta pregunta» se ofrecen solo con catálogos Manual. |
| 16 | Corregido. El aviso de títulos de la pregunta lo calcula el servidor (`POST /api/forms/catalogs/{id}/presentation-checks`) sobre todas las opciones activas, en cada idioma del catálogo. |
| 17 | Corregido. La lista del desplegable de una celda se dibuja en un portal; verificada en las visuales y en el navegador. |
| 18 | Corregido. Con filtros fijos y sin filtros por persona, un máximo de más de 20 en «elegir varias» vuelve a frenar publicar con `CatalogTooLargeForMultiple`. |
| 19 | Corregido. Un valor fijo sobre un campo de catálogo tiene que ser una opción activa de su fuente (`MatchValueUnavailable`), al publicar y en el máximo del editor. |
| 20 | Corregido. «Como si lo recibiera» exige también `formcatalogs.read`, en el back y en el front. |
| 21 | Corregido. Las sugerencias pasan `includeCatalogFields: false`. |
| 22 | Corregido en parte, como se explica arriba. Bajar `maxLength`, cambiar `min` o `max`, activar «único» o reactivar un campo acumulan los valores incompatibles con conteo y filas en su control, también en un campo inactivo y junto con un cambio de tipo. |
| 23 | Corregido. El chequeo de presentación se calcula en cada idioma del catálogo, con títulos de 30 y descripciones de 300. |
| 24 | Corregido. Los lectores pasan a `ListCatalogFieldValuesAsync` y `ListFieldUsageAsync`, `PersistenceNamingTests` detecta el desvío, y la receta de un área lo dice. |
| 25 | Corregido. «En uso» dice si es el borrador o la versión activa, no muestra el uso propio como un formulario y suma «Ir a la pregunta» de CE1 (`questionId`). |
| 26 | Corregido. La carga y el error de una persona se ven en el diálogo, incluido el 403. La tabla se actualiza al cerrar el editor, y un 409 permite «Volver a cargar el dato», con espera y error propios. |
| 27 | Corregido. `required && isActive` es igual en la página y en el hook; una opción inactiva no marca sus campos como obligatorios. |
| 28 | Corregido. «Como si lo recibiera» se puede reintentar, respeta la espera de un 429 y no vuelve a pedir solo al recuperar el foco. |
| 29 | Corregido. Los avisos de publicar son una unión discriminada por `code`, los guards no aceptan null ni topes que el contrato no manda, y las cantidades usan `_one`/`_other`. |
| 30 | Corregido. Las flechas mueven solo entre campos activos. |
| 31 | Corregido. Hay una sola pieza de formato por tipo en la tabla, la importación, el resumen, las respuestas, WhatsApp y las líneas de error. |
| 32 | Corregido. CE1: columna «#», detalle del tipo y «y sus N valores». CE2: línea de error con valor y motivo, indicación de más columnas y ayudas en el celular. CE3-B: los campos de «Como en el catálogo». CE4-A: «Como mucho N por persona» y «Todos ven las mismas N», «N personas · M sin <dato>» y el bloqueo del chat con la persona y el dato. CE5: «Columnas · N ocultas». |
| 33 | Corregido. Con PostgreSQL: quitar un campo en uso en el borrador y en la versión activa, `PersonAttribute.InUse` con borrador y con versión activa, y el asistente con una persona sin el dato. `FieldInUse` y los usos `Draft` y `Active` están exportados y los consume el front, igual que el formato canónico con cultura al importar por HTTP. |
| 34 | Corregido. El front lee los estados en minúsculas, con tipos, claves es/en y dobles realistas. Los ejemplos HTTP reales del asistente (bootstrap, enviar, turno, turnos y cancelar) se exportan y se validan con zod estricto. |
| 35 | Corregido. Catálogos (nombre, descripción y opciones), campos y datos de las personas guardan su idioma original, el del menú al crear, que es el único obligatorio. No hay selector «Textos en» ni pares de campos por idioma; con el menú en el otro idioma, el original se muestra como referencia sin copiarlo. Los respaldos ya no suponen el español, tampoco en WhatsApp ni en la planilla. Reglas en `formularios.md` y en el diseño del front. |
| 36 | Corregido. «Agregar opción» no marca nada en rojo: un vacío se marca al intentar guardar o al salir de un campo tocado, y el código repetido y el valor inválido siguen al escribir. |
| 37 | Corregido. Las acciones de «Opciones» van al margen derecho, a la altura del título, y el motivo de un deshabilitado es una ayuda (`DisabledReason`) con mouse, foco y lector de pantalla. |
| 38 | Corregido en todo el front. Botones sin textos al lado ni debajo y sin cambios de forma; sin «Cambios sin guardar» ni números de revisión fuera de los historiales; acciones al borde derecho; validación al intentar guardar. Las esperas de un 429 son el motivo del control, salvo las que las guías nombran (ingreso público, Inicio y Formularios que faltan). |
| 39 | Corregido. «El catálogo» tiene solo Nombre y Descripción (opcional en back y front), con la línea de solo lectura «Escritas a mano · Texto» arriba de Nombre. |
| 40 | Corregido. El estado podía darse con valores guardados antes de un cambio de fuente: la captura deja ese campo fuera del sello, como un campo sin valor, en lugar de responder 500. |
| 41 | Corregido. El formulario y los catálogos (Manual y de Filtros) conservan el borrador al cambiar el idioma del menú y muestran la otra traducción. El diseño del PDF todavía se reinicia y conserva el aviso de salida. |
| 42 | Corregido. El front ya mandaba `confirmValueRemoval` y aceptaba `questionId`; la exportación regeneró `fields-conversion-error` con sus filas. |
| «En uso» de los datos de las personas | Corregido con el criterio del punto 11: los nombres de los formularios solo con `formtemplates.read`; sin él, «En uso en N formularios». Regresiones de servicio, guard y pantalla. |
| FX01 | Revisado contra su brief: los commits cumplen y cada hallazgo tiene su regresión. |
| FX02 a FX06 | Corregidos. Se reprodujeron todos los hallazgos, y cada uno tiene su regresión y su commit. |

## Corrida final

**Backend** sobre `34e48bee`:
- build de la solución con **0 advertencias y 0 errores**;
- `dotnet test` completo con PostgreSQL y Redis en Docker: **6.498 de 6.498**, sin fallos ni omitidos;
- la exportación `20261025-233355-14e4887e74f04628a4ea0748e3e0c988` copió al front **23 cohortes** de esa misma corrida, sin editarlas, y quedaron commiteadas en `f330793f`.

**Front** sobre `0cf7aced`:
- **3.494 tests en 321 archivos**, en verde;
- `npm run lint` limpio, con los **51 controles de arquitectura**;
- `npm run build` sin advertencias;
- visuales **741 de 741**, sin diferencias contra las referencias;
- los consumidores de las cohortes validan con zod estricto los cuerpos reales, incluidos FieldInUse con sus usos Draft/Active y los ejemplos del asistente de Inicio (bootstrap, enviar, turno, turnos y cancelar).

**Recorrido en el navegador** con la app real:
- La app se levantó con `aspire run` (CLI 13.5.4). La Api usó el correo en carpeta local (`Email__Delivery=PickupDirectory`, solo para esa corrida) para ingresar con un código.
- Se recorrió en es y en, a 390 y 320 px, y a lo ancho para la tabla ancha:
  - **Chat de Inicio:** «¿Qué roles hay?» devolvió una respuesta real y verificada, sin claves sin traducir y sin scroll horizontal, en los dos idiomas.
  - **Catálogo en español:**
    - el alta mostró «Escritas a mano · Texto», Nombre y Descripción opcional;
    - agregar una opción no marcó nada en rojo;
    - un texto vacío se marcó recién al guardar, con el foco y a la vista.
  - **Traducción:** con una descripción sin guardar, cambiar el menú a inglés la conservó como «Original (Spanish)». Se tradujeron el nombre, la descripción y las opciones, y se guardó; en español sigue el original.
  - **Baja en la fuente:** «Proveedores recorrido E2» tiene el campo Zona, de «Zonas recorrido E2». Después de dar de baja «Norte» en la fuente, Proveedores se editó y se guardó, y conserva «Norte».
  - **Tabla ancha:** el desplegable de la celda se abrió en un portal, hacia arriba en la última fila, sin recortarse. Las acciones de «Opciones» quedaron alineadas a la derecha.
  - **Reglas y sugerencias:**
    - sin «Cambios sin guardar» ni revisiones fuera de los historiales, con las acciones a la derecha;
    - agregar una regla y una acción vacía no marcó nada hasta intentar guardar («Completá este dato.»);
    - el borrador de reglas siguió después de cambiar el idioma.
- Al terminar se volvió el idioma de la cuenta a español. `aspire stop` confirmó que no queda ningún AppHost corriendo.

## Queda para después

Además de lo de «Después, sin bloquear»:
- **El editor del formulario se reinicia y pierde lo escrito** si se cambia el idioma del menú dos veces seguidas antes de que llegue el detalle.
- **En un catálogo nuevo sin guardar**, el original sigue al idioma del menú hasta guardar.
- **Los nombres de «En uso»** quedan en el idioma anterior hasta guardar.
- **El diseño del PDF** no conserva el borrador al cambiar el idioma: lo protege el aviso de salida.
- **La clave vacía de un campo nuevo** se marca al guardar con el texto general «Revisá los datos del catálogo.», en lugar de uno propio del campo.
- **El piloto único con Meta** sigue pendiente, con el `CheckboxGroup` vacío sumado a su lista. No se inició.
