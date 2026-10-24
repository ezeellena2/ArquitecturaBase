# BusinessForms: borde HTTP

Leer [formularios](../../../../docs/features/formularios.md) y el [resumen de la etapa en curso](../../../../docs/plans/formularios/README.md).
- Seis rutas de borrador MVC; esos controllers sólo IFormDraftService. Contratos HTTP propios mapeados a mano y ToString redactado.
- Read en todas; Manage adicional al escribir. Recuperación sólo del actor, sin requerir Manage. Aceptación/status tienen rutas propias con Read+Publish; no envío/respuesta.
- Declarar esquemas OpenAPI, límite de cuerpo 512 KiB y rate limit por actor. Idempotencia no evita permisos actuales.
- Errors mediante ToActionResult/ToCreatedResult. Expiración 410 traducido; 404 de intento en vuelo no prueba rollback.

- Séptima ruta, separada: POST definitions/{id}/publication-checks consume IBusinessFormPublicationService y exige Read+Publish, sin Manage. Consulta sin efectos.
- Cuerpo tipado con ExpectedRevision/Name/Prefix/Draft y contratos anidados existentes; desconocidos rechazados. 409 para revisión obsoleta o contenido sin guardar; 200 para bloqueos de representabilidad o canal ausente.
- D5 humana adapta directamente los seis DTOs al nombre mapa es/en; el front actualiza sus guards cerrados. Sin variante legada/versionado porque el módulo no estaba desplegado. Aceptación/status adoptan el contrato revisado; preview tiene consumidor propio de lectura.

POST definitions/{id}/publications → IFormPublicationAcceptanceService: RequestId/ExpectedRevision/CheckToken y Name mapa/Prefix/Draft cerrado, 512 KiB/30 por minuto, 202+Location sólo después del commit. GET definitions/{id}/publications/{requestId} → IFormPublicationStatusService: 120 por minuto, actor propio y permisos Read+Publish actuales, 404 ajena/inexistente. Nunca loguear token ni contenido.

Décima ruta: GET definitions/{id}/versions/{versionId}/preview → IFormPreviewService, sólo Read, pertenencia/publicación y query culture explícita es/en. 120/min; Cache-Control no-store aun al fallar. URL/vencimiento provienen del proveedor, DTO redactado, sin tokens de captura ni URL de entrada. Status devuelve issues seguros sin IDs externos/JSON/credenciales.

FormPreviewQuery exige culture es/en en Application y devuelve400 por campo al faltar/no ser admitida. Detalle agrega publication seguro/capabilities; nunca expone UUID de recuperación ajeno. No-store también en los errores de preview.

Catálogos manuales: GET/POST `/api/forms/catalogs`, GET/PUT `/api/forms/catalogs/{id}` y GET `/api/forms/catalogs/{id}/items`; Read en lectura, Manage adicional en alta/edición. Mapear queries HTTP a mano. `expectedRevision` es obligatorio para PUT y los conflictos no guardan parcialmente. GET items admite `ids` repetido (`?ids=a&ids=b`, hasta 100, sin Guid vacío; el controller pasa `[]` si falta) para leer opciones por identidad, también dadas de baja si no se manda `isActive`. POST `/api/forms/catalogs/{id}/presentation-checks` chequea la presentación propia de una pregunta sin guardar: sólo Read, política de lectura y no-store; cuerpo cerrado con `presentation` y respuesta `FormCatalogPresentationCheckResponse`, la misma forma que `presentationCheck` del detalle.

E1-B: el alta Manual admite `valueType` opcional (`Text` por defecto, `Integer`, `Decimal` o `Date`); PUT no lo admite y rechaza desconocidos. Cada ítem admite `value` nullable. Las respuestas de catálogo siempre llevan `valueType` y las de ítem `value`, incluso null; los errores de normalización señalan `items[index].value`. El controller sólo mapea, sin interpretar valores.

E1-A: POST catalogs/{id}/import-previews e /imports reciben un contrato FromForm y exigen Read+Manage; el archivo vive sólo durante la llamada y se cierra al terminar. El límite HTTP es 10 MiB más 64 KiB de envoltorio; el lector conserva el tope de archivo de 10 MiB. Ambas rutas y GET catalogs/{id}/export usan forms-export, sin cola, y no-store; el XLSX suma nosniff. Export y template sólo exigen Read. PUT admite SortAlphabetically sin movimientos simultáneos.

Los requests de ítems no reciben `Position`; el servidor asigna altas al final. PUT acepta una secuencia opcional `moves: [{ itemId, direction: "up" | "down" }]`, sin escrituras por flecha y bajo el mismo Guardar/revisión.

K4 Filters: `GET /api/forms/catalog-filter-options` entrega columnas/roles, disponibilidad de la vista y muestra paginada/ordenable sólo con `CatalogsManage`; `POST /api/forms/catalog-previews` exige Read+Manage y no guarda. Alta/edición usan `POST /api/forms/catalogs/filters` y `PUT /api/forms/catalogs/{id}/filters`; detalle sigue en `GET /api/forms/catalogs/{id}`. Sólo la vista previa recibe `sampleUserId`. `GET /api/forms/catalogs` usa `state=Active|Inactive|Unavailable`; responde `filterCounts.byKind` de todo el conjunto filtrado por búsqueda/estado, independiente de origen y página.

El borrador cerrado admite files (minFiles, maxFiles, photoSource) y, sólo en multiChoice, selection (minSelected, maxSelected). Guardado, chequeo y aceptación comparten `Contracts/FormDraftRequestMapping`, que preserva también los valores ausentes para el validador; sin rutas ni permisos nuevos para autoría. Selection conserva su omisión al null en los DTO sellados.

F4: GET records/{recordId}/files/{fileId}/thumbnail y /content consumen IFormFileService y exigen formrecords.read. content recibe download opcional. No-store/nosniff también en errores; documentos como attachment y fotos en línea sólo JPEG/PNG. Inventario/OpenAPI binario y tests de 401/403/404; no loguear nombres ni bytes.

La lectura de respuestas conserva cinco operaciones: listado GET y búsqueda POST de records por definición; POST de conteos; detalle GET por record; exportación POST. Todas exigen formrecords.read; export exige además formrecords.export. Controllers sólo consumen servicios y comparten el mapeo HTTP fuera de los controllers. Exportación usa una política propia de una ejecución concurrente por usuario, sin cola; no consume el cupo de lectura. Búsqueda, personas y condiciones privadas van en cuerpos redactados de hasta 512 KiB; lecturas no-store y Excel binario nosniff. DTO/OpenAPI/inventario son el contrato, sin Markdown de contratos.

`GET /api/forms/overview` consume IFormOverviewService con RecordsRead o ApprovalsDecide mediante HasAnyPermission. Página y facetas salen del servidor; el filtro privado PendingOnly exige decidir. Quien sólo aprueba recibe únicamente formularios con tareas decidibles y sus conteos, sin respuestas ni última respuesta; conserva el segmento Activos/Dados de baja elegido. No-store y política de lectura.

`POST definitions/{definitionId}/records/{recordId}/navigation` comparte el contrato cerrado de búsqueda y su mapeo, con RecordsRead. Devuelve sólo recordId, previousId, nextId, position (desde 1) y totalCount globales; página/tamaño no acotan los vecinos. Conserva no-store, límite de cuerpo y errores existentes.

G1 agrega cinco rutas bajo definitions/{id}/rules: actual, guardar revisión, historial paginado, detalle por revisión y opciones. Requieren formrules.manage, contratos HTTP propios y no-store; guardado con expectedRevision devuelve 409 al quedar atrás.

G5 agrega GET records/{recordId}/workflow y POST actions/{runId}/consult o /retry bajo esa respuesta. Lectura requiere formrecords.read; controles suman formrules.manage, política de escritura, 202 y Location del workflow, sin ejecutar el efecto en la petición ni exponer almacenamiento.

La aprobación de un nivel conserva revisión, autor confirmado, rol y decisión. `formapprovals.decide` no concede lectura de valores o PDF; la decisión revalida rol y permiso, prohíbe al autor y confirma tarea, estado y runs juntos. El rechazo exige motivo de 1–500 caracteres. La continuación por TaskId exige Decide, DefinitionId, dirección Next/Previous y tamaño 1–100; no-store, sin offset, con ancla resuelta autorizada y sin conceder lectura de valores.

G2 guarda diseño PDF por revisión propia y `ActivePdfDesignId`, sin cambiar borrador/publicación. El run fija diseño, datos, culturas completas y nombre del sistema. La emisión usa creación condicional por run/cultura, consulta primero y conserva bytes ya subidos; metadata se confirma sólo con fence vigente. El preview usa ejemplos y no emite ni guarda. Documentos privados sólo por API con `formrecords.read` antes y después de abrir el stream.

POST actions/{runId}/review recibe únicamente wasSent obligatorio y devuelve 202 con el workflow. Comparte RecordsRead+RulesManage, no-store, límite de escritura y Location; identidad/fecha se obtienen del servidor. Review nullable y canReview forman parte del contrato de cada ejecución.
La acción expone revision=Fence; review exige expectedRevision y wasSent explícitos. Un replay idéntico conserva actor/fecha y no reabre el run; una revisión obsoleta devuelve409.

Etapa 5, editor 03: create/update de borrador aceptan offerWhen escalar nullable; Definition lo devuelve también en commit/recuperación. No agrega rutas ni permisos. Chequeo y aceptación mantienen su cuerpo cerrado sin offerWhen: este texto se guarda con el borrador y no forma parte de una publicación.

Etapa 5, s2: cinco rutas de suggestions bajo definitions/{id}: GET actual, POST nueva revisión y GET options/revisions/revisions/{revision}. Sólo formsuggestions.manage, no-store, cuerpo cerrado de 512 KiB y revisión esperada; POST devuelve 201/Location o 409. Origen sin publicar da Forms.Suggestion.SourceNotPublished. SourceVersionId nullable en el set y DestinationVersionId en cada sugerencia son metadata del servidor, nunca entrada del cliente.

H4: POST catalogs/{id}/proposal/approve y /discard consumen IFormCatalogProposalService con Read+Manage, cuerpo cerrado y expectedRevision. Aprobar recibe los textos editados, Filters y sampleUserId de la vista previa; no recibe actor, estado ni procedencia. Devuelven 200 o conflicto 409; el detalle incluye proposal nullable con evidencia original y decisión real, sin respuestas del proveedor.

H7: GET /api/forms/missing-requests y POST /{id}/discard consumen IFormMissingRequestService y exigen únicamente formtemplates.manage. La lista pagina con búsqueda y State Pending/Discarded, no-store; los conteos por estado respetan búsqueda antes del filtro de estado. Descartar recibe sólo expectedRevision en un cuerpo cerrado y devuelve la fila confirmada o409. No existe HTTP de captura de pedidos ni se admiten personas/mensajes/actor del cliente.

Baja: GET/POST definitions/{id}/deactivation sólo consumen IFormLifecycleService y exigen Read+Publish, sin Manage. GET es vista previa de impacto; POST recibe únicamente expectedRevision y devuelve la revisión propia, con 409 por publicación abierta o revisión obsoleta. No-store en ambas. Lifecycle es metadata hermana de listado/detalle, ajena al borrador y sus comprobantes. Active es el filtro predeterminado; facetas Active/Published/Drafts/Deactivated.

Eliminar: DELETE definitions/{id} consume IFormLifecycleService con Read+Manage y un cuerpo cerrado expectedRevision; devuelve204 tras el commit o409 por revisión, respuestas, envíos abiertos o publicación en curso. No-store. Lifecycle agrega LastOpenSessionExpiresAtUtc, CanDelete y DeletionUnavailableReason; los contadores siguen null sin RecordsRead.

Datos de personas: siete rutas bajo person-attributes/person-data consumen IFormPersonDataService, no-store. Atributos/opciones: leer con PersonDataManage o UsersManage o Manage; crear/editar sólo PersonDataManage. Opciones entrega id/text paginados sin CatalogsRead. Tabla requiere PersonDataManage+UsersRead y responde `FormPersonDataPageResponse` (página más `missing` por dato activo); GET/PUT por persona, UsersManage. PUT cerrado con revisión propia y delta: ausencia conserva, null limpia; nunca recibe actor/userId en cuerpo.

E2-3: Source agrega Match/Presentation en el mapeo único, con contratos cerrados y redacción. GET recipient-samples y POST definitions/{id}/recipient-previews consumen IFormRecipientPreviewService con Read+Manage+CatalogsRead (atributo de clase), no-store y políticas existentes; sólo el POST recibe persona, filtros y cultura. Publicación devuelve warnings siempre, separados de issues.
