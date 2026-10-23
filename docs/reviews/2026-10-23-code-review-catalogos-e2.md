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
35. **Crear un catálogo con el nombre y la descripción solo en español parece no guardar** (back: `FormCatalogRequestValidators.cs:183-184`; front: `CatalogMetadataFields.tsx:25,30`).
    - **Qué pasa:**
      - el back exige es y en, y el 400 trae `name.en` y `description.en`;
      - el front muestra el error solo en la pestaña del idioma activo y no avisa en ningún otro lado;
      - la persona ve que no pasa nada y, al irse, pierde lo escrito.
    - **Arreglo:**
      - **decisión del usuario, 2026-10-23:** el inglés es opcional en el nombre y la descripción del catálogo, como en los formularios (el idioma original es obligatorio y la traducción, opcional) y en las opciones. Hay que ajustar el validador, el texto de ayuda de la pantalla, los contratos y el front, y anotarlo en las reglas de formularios;
      - un error de guardado en un campo que no está a la vista tiene que verse: cambiar a la pestaña del idioma con el error y marcarla. Si no hay un campo visible, se muestra en la página.
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
- **Despliegue:** volver a una build anterior a E2 con versiones publicadas que tienen filtros mandaría las listas sin filtrar, porque el código viejo ignora `match`. Hay que anotarlo en la guía de despliegue.
- **Quitar el módulo** se verificó en una copia aislada y no quedó automatizado. Sigue pendiente la guía `quitar-formularios.md`.
- **El replay de una sesión sellada antes de E2** no tiene una prueba de punta a punta por el recorder nuevo. Por construcción, el riesgo es bajo.

## Para el piloto

- **Una pregunta opcional de «elegir varias»** filtrada que queda sin opciones para una persona manda un `CheckboxGroup` vacío. La referencia de Meta pide de 1 a 20; E1 solo confirmó el Dropdown vacío. Hay que confirmar qué hace Meta.
