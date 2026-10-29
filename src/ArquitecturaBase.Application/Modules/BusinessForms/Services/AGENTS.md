# BusinessForms/Services: casos de uso y helpers

Casos de uso y helpers; contratos en `Interfaces/Services`. Leer [formularios](../../../../../docs/features/formularios.md), [Channels](../Channels/AGENTS.md) y [Persistence](../Interfaces/Persistence/AGENTS.md).

## Al modificar

- Ejemplo (`FormLifecycleService`): guard → validador → `OnSuccess` sólo si escribe, con lock, permisos vigentes y revisión adentro. Ninguna lectura abre UoW.

### Borradores y autoría

- Read, más Manage al escribir. El comprobante propio se consulta antes que la revisión; la purga es `FormDraftRetentionService`.
- `Normalize` canoniza mapas, longitudes por defecto y `OfferWhen` (vacío es null); `Resolve` asigna identidades y falla si se pierde una traducción no vacía. `LocalId` sólo correlaciona el intento.
- `SchemaJson` lleva `format` y `fields`; cultura, botón, retiradas y `OfferWhen` van aparte. Guardar o chequear no crea campos ni versiones. MultiChoice requiere Selection: 0 ≤ min ≤ max ≤ 20, max ≥ 1 y Required si y sólo si min > 0; opciones propias de 1 a 20, con mínimo no mayor que su cantidad. Cambiar tipo crea identidad nueva; cambiar el rango la conserva.
- Listado/facetas usan cultura UI. RecoveryActor sólo con Publish; omitir borrados sin repaginar y detalle/recibo ausentes dan NotFound.

### Publicación

- Read+Publish. El chequeo es consulta: compara la entrada normalizada con lo guardado, sin `Resolve`, IDs nuevos, UoW ni locks.
- Canales ordenados por `ChannelId`: vacío o repetido lanza; ninguno da `ChannelUnavailable`.
- Sólo el original incompleto bloquea por idioma; al canal van las culturas completas. Un multiChoice de Manual bloquea con más de 20 ítems activos; el guard cuenta en SQL una vez por catálogo compartido, sin hidratar ítems. Con filtros fijos y sin filtros por persona bloquea igual (`CatalogTooLargeForMultiple` en `.catalog`) si el máximo de la oferta pasa 20, en el chequeo y en la aceptación; con filtros por persona o en Choice el máximo es aviso. Choice y Filters conservan sus recorridos.
- Aceptación: lock actor/RequestId → replay, antes del token y la revisión → lock de definiciones → revalidación → sella campos, versión, culturas de los perfiles verificados, operación y manifiesto protegido y bloquea el prefijo, sin activar ni llamar al proveedor.
- Despacho: reservar y completar son límites separados y el pedido sale de lo sellado; el canal se llama fuera de toda UoW y un error queda `Uncertain` con los mismos IDs. Se activa por `BaseActiveVersionId` y fence sin pisar un borrador posterior; `CanRetry` es false.
- Status devuelve sólo la operación propia; preview consulta al canal sin UoW.

### Ciclo de vida: dar de baja, reactivar y eliminar

- Baja: Read+Publish; lock → permisos → revisión → sin publicación abierta → `Deactivate`, que conserva la versión activa. La vista previa lee y lista sugerencias entrantes activas.
- Reactivar es publicar el borrador; Domain reactiva al activar la versión nueva.
- Eliminar: Read+Manage; revisión, sin publicación abierta, `LastRecordNumber` cero y sin envíos abiertos ni replies pendientes (`IStructuredFormChannelStateSource`, sólo lectura). `Remove` deja el borrado lógico al interceptor.
- Lifecycle fuera de borrador/manifiesto; conteos con RecordsRead y CanDelete con Manage sin revelarlos.

### Datos de personas

- `FormPersonDataService`: atributos con PersonDataManage; lectura OR UsersManage/Manage; tabla además UsersRead. La tabla (`FormPersonDataPageResponse`) suma `Missing` por atributo activo, en su orden, con el conteo del lector sobre la misma búsqueda, leído entre los dos chequeos de permiso. Valores requieren UsersManage y usuario existente, antes y dentro del guardado o después de leer.
- Atributos: lock definiciones → atributos → fuentes ordenadas, máximo20 incluyendo inactivos; usados no cambian tipo/fuente/actividad. `usedBy` nombra cada formulario sólo con `formtemplates.read` (`CanReadFormsAsync`); sin él, uno por formulario con `definitionId` y `name` null. Cambiar tipo o fuente con valores cargados de personas no borradas exige `ConfirmValueRemoval` (si no, `ValueRemovalNotConfirmed` con `count` del lector en `type` o `sourceCatalogId`); el cambio quita la clave de cada fila del repositorio, también de cuentas borradas, con su CAS en la misma UoW. Sin locks nuevos: guardar valores toma el lock de atributos antes que el de la persona, así que queda excluido, y un editor abierto recibe 409 por la revisión de su fila. Valores: atributos → persona → fuentes, CAS propio y delta (ausente conserva, null limpia). Catálogo Manual activo y opción activa; texto recortado hasta300. No se borran valores inactivos ni se devuelven. El texto de un valor de catálogo sale de `OptionTexts` sólo si el catálogo del ítem es la fuente del dato; `RecipientValues` aplica la misma comparación.
- Nombres: el original del atributo (`OriginalCulture`, el idioma del menú al crearlo) es el único obligatorio, como en los catálogos (ver Catálogos y filtros); la tabla, las opciones y la fuente se muestran en el idioma del menú y, si falta, en el otro.
- Opciones: lector paginado exclusivo id/text, sin CatalogsRead ni campos internos. Sin caché ni replay durable.

### Catálogos y filtros

- CatalogsRead, más CatalogsManage al escribir, con lock del catálogo; metadatos, estado e ítems comparten revisión, sin protocolo durable.
- Crear un catálogo o un dato de las personas fija su idioma original con la cultura del pedido (el idioma del menú); H4 usa la de la conversación. No viaja en los pedidos ni cambia después; el detalle del catálogo y los datos de las personas lo devuelven en `originalCulture`, leído de lo guardado y no de la cultura del pedido.
- Los validadores sólo revisan la forma de los mapas (claves es/en, largo; `null` o blanco es una traducción ausente). El original lo exige `FormCatalogOriginalTextPolicy` (nombre, texto de cada opción y nombre de cada campo, en ese orden) con el primer `ContentInvalid` en `name.<orig>`, `items[i].text.<orig>` o `fields[i].name.<orig>`: al crear, con la cultura del pedido antes de la UoW; al editar, con `catalog.OriginalCulture` después de leerlo: en Manual, después del chequeo de revisión; en Filters, junto con el validador y antes del chequeo de revisión de `UpdateFilters`, así que un PUT atrasado sin el original responde 400 y no 409. H4 valida con `ValidateEdits` y el mismo `catalog.OriginalCulture`. Los mapas se guardan con `FormCatalogProposalPolicy.Texts`, que descarta los blancos. La descripción es opcional en los dos idiomas (Manual, Filters y H4 al editar o aprobar; sólo la propuesta que arma el asistente la exige). Un dato de las personas exige su nombre igual, con `AttributeInvalid` en `name.<orig>`.
- Valor: entero hasta 18 dígitos, decimal exacto y fecha ISO; errores `items[i].value`, también inactivos. Texto importado usa cultura UI y rechaza ambigüedad; celdas tipadas/cabecera canónica conservan precisión. Tipo sólo al crear; mover conserva valor.
- `FormCatalogItemRecorder` comparte PUT/import sobre ítems seguidos, sin límite propio. Manual sin Position; moves y A–Z conservan los desempates; A–Z ordena por el texto original con es-AR o en-US. Un texto en blanco es un texto ausente. Error revierte todo. `Fields` es un delta sobre todo lo guardado (ausente conserva, null limpia); la importación manda cada campo activo, null si la fila no lo trae. Los valores de campos pedidos no se escriben: pasan a `ValidateItems`, único escritor de FieldValuesJson, que sólo escribe el ítem cuyo mapa cambió (`JsonElement.DeepEquals` por clave, porque jsonb vuelve con otro espaciado): así un guardado sin cambios no reescribe ni audita las 10.000 opciones.
- `FormCatalogFieldRecorder`: identidades, claves, fuentes Manual activas, usos borrador/activa y confirmaciones antes de configurar; en la misma lectura de usos que las bajas, un campo que usa un filtro (`ByMatch`) no cambia papel, tipo ni fuente (FieldInUse en esa propiedad); de un campo de catálogo que cambia de tipo o fuente, activo o no, lee también la fuente anterior, con bajas, sólo por sus textos. Una regla mal formada (`InvalidRule`) se informa en `type`, `min`, `max` o `maxLength`. Un campo inactivo que cambia tipo, fuente o reglas carga y valida su fuente igual que uno activo, y `LockCatalogsAsync` bloquea las fuentes de todos los campos del pedido. `FormCatalogFieldPolicy`: todos los valores, requeridos, unicidad activa, conversión y JSON tipado bajo lock; errores con paths y metadata acotada (FieldErrors.OnWithMetadata). Reactivar un campo o cambiar sus reglas con el mismo tipo (`ChangedRules`: `maxLength` que baja, `min` o `max` distintos, «único» que se activa) revalida lo guardado aun en un campo inactivo, que nunca es obligatorio: cada valor guardado que falla va al balde de su control (`isActive` al reactivar; si no, el que rompe o el primero que cambió) y cada repetido activo a `unique` (o `isActive`) con motivo `invalid`; se devuelve el primer balde no vacío en el orden isActive, maxLength, min, max, unique como `FieldValuesIncompatible` con `invalidCount` y hasta 50 `invalidRows` (los demás controles salen en el guardado siguiente). «Único» que se activa o se reactiva agrupa los repetidos también cuando cambian el tipo o la fuente, después del balde `type` de la conversión. Un valor fresh, o el que falta en una opción del pedido sin valor guardado (nueva o reactivada), sigue fallando en su celda, y sin esos cambios lo guardado conserva el primer error (en `type` o `unique`). Al cambiar el tipo, lo guardado como texto se convierte con `NormalizeImport` en la cultura del pedido (ambiguo no convierte); a texto, una opción o un booleano guardados pasan a su texto visible (opción fuente, «Sí»/«No») en el `OriginalCulture` del catálogo, que es también el valor intentado de una opción que no convierte a otro tipo o fuente; lo demás de otro tipo, canónico, y lo mandado en `Fields` (aunque coincida con lo guardado) se lee en el tipo nuevo. La opción fuente activa se exige sólo al valor nuevo, cambiado (distinto del guardado) o convertido; el guardado que apunta a una opción dada de baja se conserva, y el que no es opción de la fuente se rechaza.
- Fields ausente conserva configuración; inactivos conservan nombre. `FormCatalogFieldPresentation` compone metadata/títulos con la política pura y fuentes por lote; `ValuesCount` de cada campo cuenta las opciones, también las inactivas, con su clave, como la confirmación de la baja; `Check` recorre las opciones activas por Position e Id en cada idioma de `Cultures` (los del nombre del catálogo) y cuenta títulos de más de `MaxTextLength` y descripciones de más de `MaxDetailLength`, con tres ejemplos. `FormCatalogService.CheckPresentationAsync` aplica ese `Check` a la presentación propia de una pregunta sobre todas las opciones activas de la fuente: CatalogsRead, sin UoW; la presentación es obligatoria, y un catálogo que no es Manual o un campo que no está activo dan `PresentationInvalid` en `presentation`.
- `FormCatalogFieldUsagePolicy.Describe` arma los usos de formularios de `usedBy` y de los `uses` del 409 (baja y cambio de papel, tipo o fuente): con `formtemplates.read`, uno por fila; sin él, uno por formulario y etapa (Active, Draft) sin nombres ni IDs. El uso `Catalog` no pasa por ahí. Detalle, guardado e importación consultan ese permiso con el mismo criterio que `UsageCount`; al guardar o importar, dentro del límite.
- Manual: máximo 10.000 incluidas inactivas/delta bajo lock; duplicado en `items[i].code` y moves ≤ 500. Condiciones: ListAll una vez por catálogo, sin paginado ni tope de oferta.
- Import: permisos/Manual/revisión antes de parsear; preview sin UoW/archivo persistido. Apply compara SHA-256 y recompone bajo lock del catálogo y sus fuentes, ordenados como el PUT, con confirmaciones y recorder. Sin diferencias conserva revisión/marca; fila inválida conserva ítem/posición. «Texto» y «Detalle» son el original del catálogo (`FormCatalogImportSource.OriginalCulture`); la traducción va en `textEn`/`detailEn` con original es o en `textEs`/`detailEs` con original en, y la del idioma del original es columna desconocida. Repetidos por el texto original; export con el mismo reparto. Con el menú en el idioma original, plantilla y export no traen la traducción, y el archivo lleva el nombre del catálogo en el idioma del menú. Campos por clave/nombre es/en; ausente conserva valor. `FormCatalogFieldSourceIndex.Resolve` recibe el GUID guardado: resuelve primero entre activas (identidad, código, texto único) y sólo si no encuentra una, la celda que nombra la opción guardada la conserva aunque esté dada de baja. Plantilla desambigua claves fijas con `[campo]`; export `[canónico]`/`[canonical]` conserva precisión y roundtrip. Sin caché/replay durable.
- Filters: personas de ejemplo sólo con Manage; la vista previa no abre UoW ni guarda. `UsageCount` es null sin `formtemplates.read`. Al editar Manual o Filters se devuelve el conteo real; una escritura fallida o sin ese permiso no consulta el uso.
- `FormCatalogOptionsPolicy` y `FormConditionChoicePolicy` sólo leen, sin permisos de valores; `FormCatalogPreparationPolicy` toma los locks de catálogo, ordenados, sólo al enviar y en la UoW del canal.
- `FormCatalogProposalService` acepta fuera de UoW ajena, exige modo neutral Assistant con Actor igual a Subject, deduplica por `ProposalId` bajo lock y crea inactivo con la evidencia original. ChannelId sólo se compara como referencia opaca de la procedencia.

### Captura de respuestas y archivos

- Replay por huella antes de permisos; luego RecordsCreate antes y dentro del límite.
- Los archivos se verifican y suben antes del límite, con clave por recibo, campo, posición y hash; el error lleva el `FieldId`.
- UoW: recibo → definiciones → versión publicada → valores/oferta → Manual ordenados/actividad → origen → número → registro/valores/referencias/archivos/runs/recibo. Rechazo guarda sólo recibo.
- Huella: suma catálogos, metadatos de archivos y origen sólo si existen, sin cambiar huellas anteriores. MultiChoice valida GUID D distintos/ofrecidos y rango sellado, sin ordenar/deduplicar. ValuesJson lleva un array por pregunta; cada elección crea valor con Position y referencia por ItemId. Omitida sin entrada; [] explícito sin filas. Manual revalida sólo actividad de elegidas bajo los locks existentes; presentación/valor/tipo salen de la oferta.
- Origen: `OriginRecordId` y `OriginSuggestionId` de registros reales de la misma persona, validados contra la sugerencia activa del destino (no su versión); de un catálogo, sólo la opción propuesta con su presentación histórica.
- Campos extra: dos lotes (metadata/valores y textos fuente) sólo de opciones Manual elegidas bajo lock. Un valor de catálogo que no es opción de la fuente actual del campo (guardado antes de un cambio de fuente) no tiene texto visible y queda fuera del sello, como un campo sin valor. Entity y FormRecordReferenceRow sellan el mismo JSON activo/presente; la sesión no lo amplía y null conserva los bytes anteriores.
- `FormFileService`: RecordsRead antes y después de abrir el stream, pertenencia y nunca URL ni SAS.

### Planilla, detalle y Excel

- RecordsRead revalidado antes de devolver, sin UoW ni caché. Esquemas leídos una vez por versión; origen por `IStructuredRecordOriginSource`.
- `FindRecordNavigationAsync`: mismo pedido/snapshot de planilla, sólo IDs, posición global desde 1 y total sin paginar. Ancla ajena, excluida o ausente da NotFound; revocar descarta también esa ausencia.
- Overview: RecordsRead o ApprovalsDecide, revalidando dimensiones; publicados no eliminados, nombre sellado y LastRecordNumber. Pendientes por roles vigentes y sin propias. Sólo aprobar conserva segmento sin respuestas; PendingOnly explícito con ambos permisos incluye bajas. Facetas/paginado SQL.
- Celdas: `NotAsked` sin pregunta; `Unanswered` vacía, también multiChoice []. El filtro `unanswered` incluye ambas en SQL/facetas/reglas. MultiChoice conserva orden capturado, usa anyOf por intersección y no admite orden por pregunta.
- Excel: RecordsRead+RecordsExport por lote/tope. MultiChoice une con '; ' y conserva precisión canónica; Choice mantiene tipos y headers sin filas. CatalogFields elige pares pregunta/campo aparte de Columns: null todos, [] ninguno.
- Campos extra: metadata actual activa más histórica por catálogo; valores/nombres/tipos de cada celda salen del snapshot. Subcolumnas sin sort/facetas. Detalle muestra todos; PDF sólo datos; chat y sugerencias omiten Fields (`includeCatalogFields: false`). Historia sin snapshot muestra «—».

### Reglas, acciones y aprobaciones

- Reglas: RulesManage, revisión inmutable con 409, sin tocar preguntas ni publicar. El historial descarta sus JSON internos sin permiso.
- `FormActionRunRecorder` agrega runs dentro del límite de captura o decisión, con destinatarios, texto y cultura fijados, sin copiar respuestas ni esquemas; un cuerpo largo falla sin recortar.
- Despacho: reservar y completar separados, efecto afuera; un Uncertain se consulta antes de repetir y un Accepted no se reenvía. RequestApproval revalida el rol al reservar: si no existe, `Forms.Rule.RoleUnavailable`, sin tarea.
- Workflow: RecordsRead antes y después de observar el canal por lote, sin efectos ni retry si todos aceptaron. Retry, Consult y revisión humana suman RulesManage dentro del límite.
- Aprobaciones: cuenta activa, permiso y rol vigentes, nunca propias. Continuar revalida permiso tras leer y admite ancla resuelta del mismo formulario; decidir confirma tarea, estado y runs en un commit.

### PDF

- Diseño: RulesManage, revisión propia y `ActivePdfDesignId`. El preview usa ejemplos (foto ficticia) y no guarda.
- Un idioma está completo con nombre de formulario/diseño, pie, preguntas y opciones elegidas.
- Preparar fija diseño, culturas y `AppName`; ejecutar relee el registro por Id, arma un `FormPdfRenderRequest` efímero y usa `WriteOnceAsync` en `FormPdfStorageKey.Create` tras consultar.
- DocumentRecorder confirma una vez bajo fence.

### Asistente y sugerencias

- Candidatos: activa publicada, OfferWhen/revisión vigentes y RecordsCreate.
- Propuesta ligada a lectura/turno/persona; MultiChoice fuera del prellenado/mapeo, sin retirar formularios mixtos. Escalar mal formado se omite; identidad/tipo/GUID ajeno rechaza.
- Preview/prellenado/aclaración no escriben ni bloquean; envío toma locks en UoW del canal. Aclaración con resx fijo; elección sin prellenado y opción inválida retira la pregunta.
- CanMap exige mismo tipo escalar o catálogo explícito; error en mappings[i].source.
- Always sin motor, con IDs explícitos. Prepare completa sólo desde propuesta auténtica. Historial con RecordsRead antes/después, salvo Assistant con Actor=Subject.

### Formularios que faltan

- Sólo una lectura completa justifica un pedido. La privacidad se revisa al proponer y al capturar, antes y bajo el lock.
- Captura por `SourceReceiptId` opaco y modo neutral Assistant o Manual; Assistant exige Actor igual a Subject. ChannelId conserva procedencia opaca, sin interpretar IDs de otro módulo. El replay no suma ni reabre. Tablero y descarte: Manage y revisión.

### Idiomas

- Antes de ofrecer o enviar, `FormPresentationCulturePolicy` elige una cultura común; texto y detalle de un ítem sin traducción y el nombre del catálogo sin traducción usan el original del catálogo sin cambiarla, también en la cultura secundaria. Una opción no necesita texto en el idioma del formulario, y una referencia capturada (`FormRecordReferencePolicy`) se muestra en el otro idioma sin dejar incompleto ninguno.
- Los nombres que se muestran (catálogo, fuente de un dato, formulario que lo usa) usan `FormRecordPresentationPolicy.Text`: el idioma del menú y, si falta, el otro; nunca suponen el español.
- Las opciones usan `TextFor`/`DetailFor` (entidad y `FormCatalogStoredItemRow`), con el mismo respaldo; `TextIn` es el texto escrito en un idioma. La oferta (`ManualTexts`), el texto fuente sellado al capturar y el índice de opciones fuente sólo llevan los idiomas con texto.
- Las acciones recomprueban con lo capturado; quien respondió en el original no pasa al secundario.
- Las lecturas históricas usan una cultura por versión según su manifiesto; la UI, el idioma actual.

## Referencias

- [Pruebas unitarias](../../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/AGENTS.md).

### Preguntas filtradas y presentación

- Match ordenado por FieldId; vacío es null. Igualdad de identidad sólo por Source.Catalog; campos nuevos null se omiten del JSON sellado.
- OptionsPolicy aplica AND tipado antes de201, sin quitar filtros inválidos ni faltantes. Un faltante (`FormCatalogMatchPolicy.MissingPersonAttribute`, el primer filtro por persona sin valor en orden de FieldId) da `RecipientDataMissing` con `MissingData`, el nombre del dato con `FormRecordPresentationPolicy.Text` en cultura UI. Shown sólo con presentación explícita heredada/propia; originales intactos. Preview interno inspecciona exceso; envío y asistente validan límites en la cultura efectiva común.
- RecipientPreview exige Read+Manage+CatalogsRead antes/después, también en ausencias (`FormDraftAccessGuard.CheckRecipientPreviewAsync`); sólo muestra atributos usados con valores de `FormCatalogMatchPolicy.RecipientValues` (un valor que no es opción de la fuente no aparece). `FormRecipientMaximumService` (el máximo por persona del editor, con el mismo chequeo) sigue permisos → validador → catálogo Manual activo (`CatalogInvalid` en `source.catalog`) → atributos sólo con filtros por persona → `FormCatalogMatchPolicy.Validate` (en `source.match`) → `FormCatalogRecipientMaximumPolicy.CheckFixedValuesAsync`, el mismo chequeo del guard (`MatchValueUnavailable` en `source.match`) → `FormCatalogRecipientMaximumPolicy.FindAsync` en cultura UI → permisos otra vez; el error del catálogo o del Match, leído después del catálogo, también se devuelve sólo si los permisos siguen. No UoW ni locks.
- PublicationGuard bloquea configuración/títulos inválidos en culturas completas. Un valor fijo sobre un campo de catálogo que no es opción activa de su fuente da `MatchValueUnavailable` en `.match` (`FormCatalogRecipientMaximumPolicy.CheckFixedValuesAsync`, compartido con el máximo del editor), antes de la presentación y del máximo; la fuente se lee con el mismo caché que la presentación. Warnings separados: máximo agrupado con filtros fijos y personas sin cualquiera de los datos (conteo único, hasta5 nombres); nunca frenan aceptar. El máximo y la etiqueta de su grupo más grande salen de `FormCatalogRecipientMaximumPolicy`, la misma para el aviso, el bloqueo y el máximo del editor. Pasado 20, el máximo de un multiChoice sin filtros por persona no es aviso: bloquea (ver Publicación). Aceptación revalida configuración vigente.
