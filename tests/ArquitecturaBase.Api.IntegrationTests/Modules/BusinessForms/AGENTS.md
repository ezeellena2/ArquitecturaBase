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

FormActionBackgroundServiceTests prueba el bucle técnico real con ServiceCollection, scopes y reloj controlado, sin ApiFactory ni Docker. Comprueba que candidatos inciertos o con excepción no frenan el lote; no acredita persistencia ni transporte externos.

La captura de catálogo comprueba que PostgreSQL conserva el mapa ofrecido y devuelve etiquetas, detalles y nombre históricos es/en aun después de editar el catálogo. La migración agrega una columna nullable sin reescribir datos existentes.

FormPdfRendererTests también ejecuta FormPdfPreviewIssuer con Skia real: nombre alternativo ausente cae entero al original, muestra la foto de ejemplo y no abre archivos privados. Es una prueba técnica sin ApiFactory ni Docker; exporta el PDF para revisar su PNG con Poppler.

La paridad entre condiciones y SQL compara ambos resultados contra conjuntos numéricos esperados, no sólo entre sí. Para tres respuestas de una versión sin pregunta y dos posteriores (una respondida y otra vacía), answered=[4], unanswered=[1,2,3,5] y notAsked=[1,2,3]; las facetas son 1/4/3 y la celda histórica sigue NotAsked.

D2 acredita por HTTP la fecha real de solicitud, distinta de la aceptación previa del run, antes y después de una decisión. Un pedido fallido por rol ausente conserva su acción, sin tarea ni estado pendiente ficticios. El origen inexistente permanece null. Los ejemplos de Workflow se exportan sólo desde los productores al cierre.

FormWorkflowEndToEndHttpTests recorre captura real con foto/catálogo histórico, aprobación por otra cuenta y PDF Skia privado con avisos completos en inglés o fallback entero al original. Sólo el canal de aviso acepta ficticiamente: Accepted no inventa DeliveredAtUtc/ReadAtUtc. Exporta PDF/workflow para el cierre conjunto; no acredita Meta físico. FormExportRateLimitTests es técnico sin ApiFactory/Docker y verifica liberación del permiso tras éxito, error conocido, excepción y cancelación, esperando la salida del pipeline completo.

D2 verifica Estado contra conjuntos fijos 1/2/3,5/4 en GET/POST/total/Excel y conserva ese filtro en las facetas existentes. La presencia PDF diferencia aprobada con copia fija de aprobada sin copia, mantiene HasApproval histórico y se obtiene por un EXISTS de página con cantidad de consultas independiente de las filas.

FormPdfEmojiTests verifica seis grafemas (simple, modificador, ZWJ y bandera) con shaping real y bitmap: contorno no vacío, píxeles iguales a Noto Emoji y avance igual al usado por el layout. FormPdfRendererTests emite un PDF mixto con la fuente embebida en nombres, pregunta, respuesta, autor, aprobación y pie. El cierre técnico revisa PNG de Poppler y texto extraído; la ejecución Windows no acredita el nativo Linux de HarfBuzz.
