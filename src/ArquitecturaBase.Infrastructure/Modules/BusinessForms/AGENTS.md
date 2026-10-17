# BusinessForms: persistencia y protección

Leer [formularios](../../../../docs/features/formularios.md).
- EF de negocio en Repository/Reader; sólo el lector usa AsNoTracking. Configuraciones y registro propios del módulo.
- Locks: actor/UUID primero y luego definiciones/prefix. Claves sólo en FormAdvisoryLockKeys. Por ahora serializa escrituras de borrador globalmente; medir antes de afinar.
- DataProtection con propósito propio v1 vinculado a fila/actor/UUID/definición/kind. Codec tipado cerrado; huella SHA256 sólo dentro del resultado protegido, jamás en columnas/logs.
- Purga seguida en lotes de 100: IUnitOfWork, sin ExecuteUpdate. Worker cada 15 minutos y al guardar; vencimiento bloquea lectura aunque falte purga física.
- La inicial agrega Definition/DraftChange; la aditiva de aceptación agrega Fields/Versions/PublicationOperations y ActiveVersionId con pertenencia compuesta. Anillo externo protegido compartido de la plantilla; ninguna credencial IA o Meta.

NameJson autoritativo; Name derivado original. DisplayNameEs/En calculados por PostgreSQL sostienen búsqueda/orden/facetas coherentes con el nombre visible de UI, con referencia original sólo si la traducción está pendiente. No edición independiente ni orden/paginación en memoria. D5 regenera únicamente la inicial no commiteada; no variantes de recibo legadas ficticias ni update de base local.

Aceptación durable: tres entidades con configuraciones propias, repositorio seguido y lector sin tracking. Constraints de actor/UUID, versión/definición/número, una operación abierta y ActiveVersionId de la misma definición. Version.SchemaJson jsonb inmutable; manifiesto y ticket protegidos con propósitos independientes, sin expiración heredada de DraftChanges. No aplicar migraciones al AppHost desde EF de diseño.

Worker de publicación: scopes separados reserva → dispatcher de canales de Application → completar; el worker no construye snapshots ni decide estados. Sin contexto/lock capturado durante transporte. Reserva sólo global existente. Activación pertenece a Definition, con CAS/base/fence actuales. Listado/facetas usan ActiveVersionId en SQL; QuestionCount del borrador. Las migraciones ya aplicadas no se regeneran.

Detalle: lector proyecta activa y última operación sin abrir manifiestos. RequestId se selecciona sólo con recoveryActor autorizado. Listado usa subconsulta SQL de última operación; filtro/facetas siguen ActiveVersionId, no el badge. Sin consultas por fila ni paginado en memoria.

Persistence/Seed/FormVersionPresentationSeeder completa sólo presentación legacy ausente desde el manifiesto protegido correspondiente. Participa del seed técnico existente por IDatabaseSeedParticipant, sin UoW propio/SaveChanges/Meta ni reescritura de schema/hash/operaciones. Version DisplayNameEs/En calculadas soportan búsqueda/orden SQL de la presentación sellada.

Records, RecordValues y CaptureReceipts tienen configuración y repositorio propios. RecordReader lee sin seguimiento la versión/presentación selladas y el recibo confirmado; FK compuestas garantizan que definición, registro, versión y campo pertenezcan al mismo formulario.

Los writes del catálogo Manual serializan por lock del catálogo. `BusinessFormCatalogItems(CatalogId, Position)` tiene clave única diferida en PostgreSQL, necesaria para swaps dentro de una UoW; no renumerar datos ajenos en la migración. `ApplySort` conserva Position+Id como orden determinista.

K4 Filters persiste la definición en `FiltersJson` jsonb con CHECK por `Kind`. `FormCatalogFilterReader` arma un SELECT fijo sobre `AspNetUsers`, `AspNetUserRoles` y `AspNetRoles` con la conexión normal: sin vista persistente, excluye cuentas borradas y nombres vacíos, y ofrece todos los roles por Id con su nombre actual. La muestra usa `SqlQueryRaw`, paginado común, `ApplySort` y desempate por Id. La vista previa compila sólo columnas y orden permitidos, enlaza valores como parámetros, limita el comando a 3 s y lee como máximo 201 filas para detectar el tope fijo de 200.

K7: BusinessFormRecordReferences tiene pertenencia compuesta a registro/campo y FK al catálogo, con ItemId opaco para Manual/Filters; no FK de Filters a ítems Manual. RecordReader carga referencias por lote y proyecta el texto realmente ofrecido. El lock de captura Manual usa la misma clave de actualización del catálogo, antes de numerar; los lectores no recomponen referencias desde datos actuales.

BusinessFormRecordFiles tiene pertenencia compuesta a registro y pregunta y una referencia única a StoredFile. La lectura agrega metadatos por lote; nombres/hashes/claves no van a diagnósticos ni previews del listado de chats. La retención del canal no toca estas tablas.

Planilla: `FormResponseReader` compone filtros tipados y conteos en SQL antes de `ApplySort`/`ToPagedResultAsync`; el desempate es Number único por definición. Reutiliza los esquemas tipados de la definición y el lector de registros para hidratar referencias y archivos por lote. Las facetas toman etiquetas de la última versión, ordenan versiones por número e ignoran sólo su propia dimensión. Las facetas de catálogo salen de referencias históricas; las proyecciones intermedias usan propiedades inicializadas para que EF pueda componer búsqueda/orden. El SQL fijo de opciones se parametriza; no habilita SQL configurable.

`Export/ClosedXmlFormWorkbookWriter` implementa el puerto de salida con ClosedXML 0.105.1 (MIT): tipos Excel, texto literal sin fórmulas, sin Office ni procesos externos en el servidor. La dependencia queda sólo en Infrastructure y su prueba técnica; no agrega persistencia.

G1: revisiones de reglas inmutables con índice único por definición/revisión y pertenencia compuesta del puntero activo. Lectores materializan metadata sin valores privados; el repositorio no guarda. El mismo lock de definiciones serializa el alta y el movimiento del puntero, sin un mecanismo nuevo.

G5: índices idempotentes por respuesta/evento/regla/acción y documento por run/cultura. Dependencias sólo dentro de la regla. El worker procesa el lote reservable completo, por NextCheckAtUtc o AcceptedAtUtc y con desempate estable; cada candidato usa scopes distintos para reservar, efecto y confirmar. Una falla técnica no saltea los candidatos siguientes. Uncertain no se sondea ni reserva automáticamente: sólo un pedido explícito lo pone en Running para reconciliar con el mismo RunId. Los efectos conocidos pendientes conservan su próxima consulta. Tests multihost se reservan a efectos y recuperación, no a configuración interna.

La aprobación de un nivel conserva revisión, autor confirmado, rol y decisión. `formapprovals.decide` no concede lectura de valores o PDF; la decisión revalida rol y permiso, prohíbe al autor y confirma tarea, estado y runs juntos. El rechazo exige motivo de 1–500 caracteres.

La retención de PDF consulta por lote los IDs GeneratePdf solicitados y su respuesta existente; no materializa snapshots ni valores, ni hace una consulta por blob. Los metadatos aún no confirmados no autorizan a la limpieza a borrar la copia de un run durable.

G2 guarda diseño PDF por revisión propia y `ActivePdfDesignId`, sin cambiar borrador/publicación. El run fija diseño, autor, decisión, culturas completas y nombre del sistema; los datos inmutables se leen por Id. La emisión usa creación condicional por run/cultura, consulta primero y conserva bytes ya subidos; metadata se confirma sólo con fence vigente. El preview usa ejemplos y no emite ni guarda. Documentos privados sólo por API con `formrecords.read` antes y después de abrir el stream.

La revisión de entrega conserva el pedido pendiente y la evidencia humana en ActionRun. El reader proyecta resultados por destinatario y atribución como FormAuthorResponse, sin cuerpos/teléfonos/claves. No infiere evidencia individual de estados agregados históricos. Sólo ofrece retry sobre Failed; la decisión se confirma por el worker, sin invocar un escritor de otro módulo dentro de la transacción.

RecordReference.TranslationsJson es jsonb nullable y se hidrata junto con las referencias por lote. La columna aditiva no reconstruye traducciones de capturas previas ni reescribe snapshots terminales de ejecuciones.

La hidratación de respuestas consulta filas livianas sin NameJson/SchemaJson por registro. Lista y export reutilizan el mapa de versiones de FormResponseDefinitionRow; chat/detalle sin contexto cargan sólo versiones distintas una vez por llamada. Facetas y orden de catálogo usan el mismo SQL fijo parametrizado para textos históricos: cultura UI, es y texto legado, sin catálogo actual ni columnas calculadas. El export conserva el estado real del registro.

Presencia: unanswered filtra y cuenta ausencia de valor/archivo incluso si la pregunta no existía en la versión. notAsked sigue usando las versiones sin ese FieldId; answered sigue exigiendo valor o archivo. Esos conjuntos pueden superponerse; no convertir la celda NotAsked en Unanswered.

El historial G1 proyecta RulesJson actual y predecesor inmediato por DefinitionId y revisión menor en el mismo SELECT paginado, con subconsulta correlacionada; el count normal del paginador se conserva. No hay lookup por fila, resumen persistido ni migración. Application calcula los contadores y nunca expone esos JSON en el contrato público.

Etapa 5: FormDefinitions.OfferWhen es una columna nullable de hasta 1000 caracteres, proyectada en el detalle y guardada con el borrador. La migración aditiva no reescribe versiones, manifiestos, hashes ni bindings publicados. La ausencia en comprobantes anteriores permanece deshabilitada.
