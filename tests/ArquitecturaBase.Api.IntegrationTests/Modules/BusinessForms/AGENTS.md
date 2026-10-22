# BusinessForms: garantías reales

Leer [formularios](../../../../docs/features/formularios.md). Usar ApiFactory, PostgreSQL/Redis reales y scopes propios.
- Verificar seis rutas/inventario, permisos y actor, snapshot anterior, retired IDs/traducciones y transacción única.
- Carreras coordinadas con lock PostgreSQL y dos hosts; comprobar espera, ganador/replay y cantidad de recibos.
- FakeTimeProvider para expiración. Restaurar anillo cifrado de PostgreSQL con certificado de test; propósito/actor equivocados no abren payload.
- Medir 40x50 y tombstone indicando qué costos excluye. Código sólo para pruebas en TestFeatures, sin proveedores externos.

- Chequeo: sesión/Read+Publish sin Manage, revisión e identidad guardadas, cuerpo cerrado, errores traducidos, ausencia real del adaptador y ausencia de escrituras. Mantener recorridos de seis rutas; D5 actualiza name a mapa y admite secundarios pendientes, sin contratos legados ficticios.
- Usar actores propios por escenario para no acumular el rate limit real entre pruebas. No subir cuotas ni quitar middleware para lograr verde. OpenAPI/inventario incluyen la consulta y aceptación/status.

D5: mapas en todas las respuestas/comprobantes, vaciados explícitos/replay/IDs, búsqueda/orden/facetas por idioma UI, migración inicial sobre esquema anterior/base vacía y Down. No usar la base AppHost ni inventar datos v1 históricos.

2b mínimo: aceptación/replay tras edición/expiración, rollback técnico sin Fields/Version/Operation/prefix parciales, actor/token/permisos, dos hosts y una operación abierta. El canal ReadyStructuredFormChannel sólo vive en TestFeatures: no acredita Meta real.

2b prueba activación preservando borrador, CAS/fences/replay, otro host y ambos workers reales con proveedor de TestFeatures: es/en, upload con errores, caída después de Create y revocación. URLs de preview/estado sin secretos; inventario/OpenAPI diez rutas. Sin red Meta; workers automáticos apagados y ejecución controlada por operación.

Mínimo2: recarga propia/ajena y revocación Publish; última operación sin borrar activa anterior/listado/facetas. Preview es/en independiente de Accept-Language, cultura ausente/ajena400, expiración sin URL en canal real y guard neutral, renovación sólo por GET nuevo.

La captura de mínimo 3 usa PostgreSQL/Redis reales para verificar valores tipados, recibo idempotente, conflicto de replay y pertenencia compuesta del FieldId. No usa el sink/source ficticio de los tests HTTP de WhatsApp.

Catálogo Manual: pruebas HTTP con PostgreSQL real cubren alta positionless, rechazo de Position enviado por cliente, secuencias de movimientos entre páginas, unicidad final y auditoría; un movimiento fuera del límite revierte metadatos/orden.

K4 Filters: `FormCatalogFiltersEndpointsTests` comprueba con PostgreSQL/Redis las opciones de roles/personas, orden/página de la muestra, preview por rol/activo/usuario elegido, permisos Read/Manage, persistencia de filtros, detalle y 409 por revisión. Verifica que los roles sin miembros aparecen, que una cuenta sin nombre se excluye y que al renombrar un rol se conserva su RoleId y se muestra el nombre actual. El lector consulta las tablas actuales de Identity; no hay vista ni fixture de vista para `EnsureCreated`. `FormCatalogEndpointsTests` verifica conteos `ByKind` sobre el conjunto completo, aun con filtro de origen y página. K6 verifica que lista y detalle cuentan una vez cada definición aunque varias preguntas refieran al mismo catálogo.

Etapa 3: filtros, orden, conteos y paginado con PostgreSQL; preguntas históricas y opciones retiradas; 401/403 y revocación; exportación tipada y rechazo sobre 10.000. `FormResponseHttpTests` ejecuta EXPLAIN sobre el comando y los parámetros reales de EF con 10.001 respuestas, verifica encabezados en inglés, excluye borradores reales y conserva un snapshot de versiones ante una captura concurrente; `FormCatalogCaptureIntegrationTests` prueba búsqueda/orden de facetas históricas sin materializar en memoria. Sin pruebas de varios hosts para estas lecturas.
`FormRecordNavigationQueryTests` comprueba SQL de sólo IDs sin paginado exterior y los extremos/posición sin Docker. `FormRecordNavigationHttpTests` prepara el recorrido real de conjunto filtrado fuera de página, desempates, permisos y errores; su ejecución PostgreSQL y ocho capturas quedan para el cierre conjunto.

FormActionBackgroundServiceTests prueba el bucle técnico real con ServiceCollection, scopes y reloj controlado, sin ApiFactory ni Docker. Comprueba que candidatos inciertos o con excepción no frenan el lote; no acredita persistencia ni transporte externos.

La captura de catálogo comprueba que PostgreSQL conserva el mapa ofrecido y devuelve etiquetas, detalles y nombre históricos es/en aun después de editar el catálogo. La migración agrega una columna nullable sin reescribir datos existentes.

FormPdfRendererTests también ejecuta FormPdfPreviewIssuer con Skia real: nombre alternativo ausente cae entero al original, muestra la foto de ejemplo y no abre archivos privados. Es una prueba técnica sin ApiFactory ni Docker; exporta el PDF para revisar su PNG con Poppler.

La paridad entre condiciones y SQL compara ambos resultados contra conjuntos numéricos esperados, no sólo entre sí. Para tres respuestas de una versión sin pregunta y dos posteriores (una respondida y otra vacía), answered=[4], unanswered=[1,2,3,5] y notAsked=[1,2,3]; las facetas son 1/4/3 y la celda histórica sigue NotAsked.

D2 acredita por HTTP la fecha real de solicitud, distinta de la aceptación previa del run, antes y después de una decisión. Un pedido fallido por rol ausente conserva su acción, sin tarea ni estado pendiente ficticios. El origen inexistente permanece null. Los ejemplos de Workflow se exportan sólo desde los productores al cierre.

FormWorkflowEndToEndHttpTests verifica captura real con foto/catálogo histórico, aprobación por otra cuenta y PDF Skia privado: faltante exclusivo de aviso o pie PDF exige respaldo entero al original; texto/detalle de ítem sin inglés conserva su original individual. Sólo el canal de aviso acepta ficticiamente: Accepted no inventa DeliveredAtUtc/ReadAtUtc. El cierre de backend verificó el recorrido con PostgreSQL y exportó PDF/workflow; no acredita Meta físico. FormExportRateLimitTests es técnico sin ApiFactory/Docker y verifica liberación del permiso tras éxito, error conocido, excepción y cancelación, esperando la salida del pipeline completo.

FormPublicationLanguageTests verifica tres versiones con original solo, secundaria completa y secundaria retirada para originales es/en. Afirma culturas del chequeo, manifiesto, activa y status; captura dieciocho cuerpos HTTP íntegros sin ticket para el exportador existente. El cierre de backend ejecutó los casos con PostgreSQL y exportó esos cuerpos sin reconstruirlos.

D2 verifica Estado contra conjuntos fijos 1/2/3,5/4 en GET/POST/total/Excel y conserva ese filtro en las facetas existentes. La presencia PDF diferencia aprobada con copia fija de aprobada sin copia, mantiene HasApproval histórico y se obtiene por un EXISTS de página con cantidad de consultas independiente de las filas.

FormPdfEmojiTests verifica seis grafemas (simple, modificador, ZWJ y bandera) con shaping real y bitmap: contorno no vacío, píxeles iguales a Noto Emoji y avance igual al usado por el layout. FormPdfRendererTests emite un PDF mixto con la fuente embebida en nombres, pregunta, respuesta, autor, aprobación y pie. El cierre técnico revisa PNG de Poppler y texto extraído; la ejecución Windows no acredita el nativo Linux de HarfBuzz.

OfferWhen se verifica por HTTP con guardado/recuperación/replay normalizado, limpieza, conflicto de revisión sin guardado parcial y rechazo del campo en publication-checks. PostgreSQL conserva el schema y no crea versiones por guardar las instrucciones.

C2: FormAssistantSelectionTests comprueba con PostgreSQL la lectura de instrucciones/revisión actuales y preguntas publicadas aunque el borrador cambie, el descarte al deshabilitar OfferWhen y el prellenado sin crear una respuesta ni una versión nueva. Se ejecuta junto con el cierre integrado.

FormSuggestionHttpTests prepara las cinco rutas con 401/403, guardado/historial propio y 409, destino publicado con OfferWhen vacío, exclusión de borradores, origen sin publicar y propiedades desconocidas. FormSuggestionContractCapture exporta ocho cuerpos íntegros mediante FORM_SUGGESTIONS_HTTP_OUTPUT; la ejecución PostgreSQL y exportación se hacen sólo en el cierre conjunto.

H5 prepara con PostgreSQL dos formularios realmente publicados, valida la pregunta y nombres actuales por el puerto neutral y comprueba que deshabilitar una instrucción invalida la aclaración completa. No crea respuestas ni acredita botones o envíos de Meta; se ejecuta en el cierre integrado.

FormSuggestionCaptureIntegrationTests prepara captura y prellenado reales por los puertos del módulo, destino publicado sin OfferWhen, aislamiento del autor, replay, valor corregido y ramas/ancestros en PostgreSQL. El GET conserva modo histórico aunque cambie el set activo y exporta es/en sin IDs técnicos del motor. Se ejecuta sólo junto con la migración y el cierre integrado.

FormAssistantMigrationTests recorre Up/Down/Up desde EnableWhatsAppAssistant con datos guardados del esquema anterior. Conserva snapshot y numeración, false tipado, actor/recibo y catálogo bilingüe; las nuevas atribuciones y punteros quedan null. Se compila por corte y se ejecuta sólo en la integración final.

La regresión de descubrimiento ejecuta los tres métodos reales de FormSelectionReader en PostgreSQL: ListAssistant, FindAssistant y ListSuggestionTargets, con orden por definición, instrucciones actuales, esquema publicado, borrador excluido y destino de sugerencia sin OfferWhen. Deshabilitar instrucciones retira sólo la oferta del asistente y no el destino publicado de sugerencias.

FormSelectionQueryTests caracteriza el SQL de selección activa, publicación histórica e instrucciones adicionales del asistente con Npgsql, sin abrir conexión ni usar Docker. No acredita persistencia de la baja o el borrado; esas comprobaciones requieren el modelo real y la integración del cierre.

FormSelectionQueryTests conserva la base histórica publicada sin el filtro de baja y exige ese filtro sólo en la selección ofertable. FormDefinitionQueryTests verifica traducción Npgsql de Active/Published/Drafts/Deactivated, orden y auditoría por revisión sin ModifiedAtUtc ni resultados protegidos. Ambos son técnicos sin conexión; los resultados, conteos, autores y empates de fecha se comprueban con PostgreSQL en el cierre.

FormDeactivationHttpTests prepara baja por Read+Publish sin Manage, CAS, cuatro filtros/facetas y privacidad de conteos. Usa publicaciones y capturas reales del módulo para conservar historia y numeración, opciones BA6 sin alterar conjuntos y el inverso BA3 sólo desde conjuntos actuales de orígenes ofertables. Baja y Guardar posterior comparten el mismo FakeTimeProvider: la revisión distingue el último autor. La reactivación conserva la baja en Accepted y la limpia al activar Published. Se ejecuta con PostgreSQL sólo en el cierre integrado; no acredita sesiones ni deprecación física de Meta.

FormDeletionHttpTests prepara eliminación HTTP de borrador o publicado sin respuestas con el interceptor real, prefijo reservado y recuperación del recibo anterior. Conserva conjuntos hacia destinos eliminados como Deleted y permite quitarlos; las respuestas capturadas y publicaciones Accepted bloquean la eliminación. El puerto neutral controlado cubre el bloqueo por sesión abierta o recibo pendiente, sin afirmar la consulta de WhatsApp ni el efecto externo. Sólo se ejecuta con PostgreSQL en el cierre integrado.

FormRetirementSourceIntegrationTests prueba la autoridad neutral con versiones reemplazadas, fallidas y eliminadas, bloqueo durante cualquier publicación abierta y pertenencia exacta DefinitionId/VersionId. FormRetirementStatusHttpTests comprueba que listado, editor y preview comparten los tres estados locales del canal y que sus metadatos no revelan contadores sin RecordsRead. La persistencia y conciliación de Meta se verifican en WhatsApp; estos escenarios no la simulan.

FormOverviewQueryTests comprueba la proyección SQL de presentación sellada y culturas publicadas sin esquemas ni manifiestos; no abre conexión. La cobertura PostgreSQL preparada conserva búsqueda/facetas sin protector, excluye borrador, primera publicación abierta y eliminado, y mantiene la activa durante un reemplazo abierto. FormVersionPresentationTests prepara aceptación y backfill idempotente desde perfiles originales en es/en, incluida metadata de culturas ausente con la presentación ya sellada; conserva schema, hash y manifiesto. Se ejecuta con PostgreSQL sólo en el cierre integrado.
