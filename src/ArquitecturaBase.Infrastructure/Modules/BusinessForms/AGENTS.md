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

G5: índices idempotentes por respuesta/evento/regla/acción y documento por run/cultura. Dependencias sólo dentro de la regla. El worker usa scopes distintos para reservar, efecto y confirmar; reconsulta lo incierto con el mismo RunId. Tests multihost se reservan a efectos y recuperación, no a configuración interna.
