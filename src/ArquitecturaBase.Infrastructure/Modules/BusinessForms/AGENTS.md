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

K4 Filters persiste la definición en `FiltersJson` jsonb con CHECK por `Kind`. `BusinessFormCatalogUsers` es una vista versionada por migración con una fila por usuario y sólo Id, nombre, roles y activo. El lector comprueba su disponibilidad con la conexión normal y proyecta personas mediante el tipo EF keyless; la muestra usa paginado común, `ApplySort` y desempate por Id. La vista previa compila columnas/orden permitidos y enlaza valores como parámetros con la conexión normal; timeout de 3 s y máximo 201 filas para detectar el tope de 200.
