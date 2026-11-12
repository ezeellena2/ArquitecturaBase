# Controllers: rutas de Formularios

Un controller por familia de rutas, con las interfaces de servicio de esa familia. Acá están las rutas, los permisos, las políticas, los límites, los encabezados y las respuestas de borrador, publicación, vista previa, baja, eliminación, catálogos y el flujo de una respuesta. La vista previa de destinatarios y la carga desde la app se describen con sus contratos en [Contracts](../Contracts/AGENTS.md); las demás familias, en el [detalle por tema](../../../../../docs/features/formularios-borde-http.md).

## Al modificar

### Las seis rutas de borrador

- Read en todas; Manage adicional al escribir. Recuperación sólo del actor, sin requerir Manage. Aceptación/status tienen rutas propias con Read+Publish; no envío/respuesta.
- Declarar esquemas OpenAPI, límite de cuerpo 512 KiB y rate limit por actor. Idempotencia no evita permisos actuales.
- Errors mediante ToActionResult/ToCreatedResult. Expiración 410 traducido; 404 de intento en vuelo no prueba rollback.

### Publicación, vista previa, baja y eliminación

- Séptima ruta, separada: POST definitions/{id}/publication-checks consume IBusinessFormPublicationService y exige Read+Publish, sin Manage. Consulta sin efectos.
- Cuerpo tipado con ExpectedRevision/Name/Prefix/Draft y contratos anidados existentes; desconocidos rechazados. 409 para revisión obsoleta o contenido sin guardar; 200 para bloqueos de representabilidad o canal ausente.

POST definitions/{id}/publications → IFormPublicationAcceptanceService: RequestId/ExpectedRevision/CheckToken y Name mapa/Prefix/Draft cerrado, 512 KiB/30 por minuto, 202+Location sólo después del commit. GET definitions/{id}/publications/{requestId} → IFormPublicationStatusService: 120 por minuto, actor propio y permisos Read+Publish actuales, 404 ajena/inexistente. Nunca loguear token ni contenido.

Décima ruta: GET definitions/{id}/versions/{versionId}/preview → IFormPreviewService, sólo Read, pertenencia/publicación y query culture explícita es/en. 120/min; Cache-Control no-store aun al fallar. URL/vencimiento provienen del proveedor, DTO redactado, sin tokens de captura ni URL de entrada. Status devuelve issues seguros sin IDs externos/JSON/credenciales.

Baja: GET/POST definitions/{id}/deactivation sólo consumen IFormLifecycleService y exigen Read+Publish, sin Manage. GET es vista previa de impacto; POST recibe únicamente expectedRevision y devuelve la revisión propia, con 409 por publicación abierta o revisión obsoleta. No-store en ambas. Lifecycle es metadata hermana de listado/detalle, ajena al borrador y sus comprobantes. Active es el filtro predeterminado; facetas Active/Published/Drafts/Deactivated.

Eliminar: DELETE definitions/{id} consume IFormLifecycleService con Read+Manage y un cuerpo cerrado expectedRevision; devuelve204 tras el commit o409 por revisión, respuestas, envíos abiertos o publicación en curso. No-store. Lifecycle agrega LastOpenSessionExpiresAtUtc, CanDelete y DeletionUnavailableReason; los contadores siguen null sin RecordsRead.

### Catálogos

Catálogos manuales: GET/POST `/api/forms/catalogs`, GET/PUT `/api/forms/catalogs/{id}` y GET `/api/forms/catalogs/{id}/items`; Read en lectura, Manage adicional en alta/edición. Mapear queries HTTP a mano. `expectedRevision` es obligatorio para PUT y los conflictos no guardan parcialmente. GET items admite `ids` repetido (`?ids=a&ids=b`, hasta 100, sin Guid vacío; el controller pasa `[]` si falta) para leer opciones por identidad, también dadas de baja si no se manda `isActive`. POST `/api/forms/catalogs/{id}/presentation-checks` chequea la presentación propia de una pregunta sin guardar: sólo Read, política de lectura y no-store; cuerpo cerrado con `presentation` y respuesta `FormCatalogPresentationCheckResponse`, la misma forma que `presentationCheck` del detalle.

E1-A: POST catalogs/{id}/import-previews e /imports reciben un contrato FromForm y exigen Read+Manage; el archivo vive sólo durante la llamada y se cierra al terminar. El límite HTTP es 10 MiB más 64 KiB de envoltorio; el lector conserva el tope de archivo de 10 MiB. Ambas rutas y GET catalogs/{id}/export usan forms-export, sin cola, y no-store; el XLSX suma nosniff. Export y template sólo exigen Read. PUT admite `sort` (columna y sentido) sin movimientos simultáneos.

Planillas de la IA (Etapa 2): POST catalogs/{id}/assistant-import-previews y /assistant-imports importan desde la copia de la planilla de una propuesta, en el mismo controller, con forms-export, no-store y Read+Manage. Reciben JSON cerrado (512 KiB): `requestId`, `proposalId` y `expectedRevision`, y al aplicar además `sheetSha256` y las confirmaciones; responden la misma vista previa (`FormCatalogImportPreviewResponse`) y el mismo recibo que subir un archivo. GET catalogs/{id}/assistant-import, sólo con Read, da la importación pendiente (`FormCatalogAssistantImportResponse`: `pending` null si no hay nada, nunca 404 por eso), así el detalle del catálogo no cambia de forma. Lleva en la acción la política de lectura (forms-read) y no forms-export, porque el front la pide al abrir el catálogo y no debe dar 429 mientras la persona exporta o revisa.

K4 Filters: `GET /api/forms/catalog-filter-options` entrega columnas/roles, disponibilidad de la vista y muestra paginada/ordenable sólo con `CatalogsManage`; `POST /api/forms/catalog-previews` exige Read+Manage y no guarda. Alta/edición usan `POST /api/forms/catalogs/filters` y `PUT /api/forms/catalogs/{id}/filters`; detalle sigue en `GET /api/forms/catalogs/{id}`. Sólo la vista previa recibe `sampleUserId`. `GET /api/forms/catalogs` usa `state=Active|Inactive|Unavailable`; responde `filterCounts.byKind` de todo el conjunto filtrado por búsqueda/estado, independiente de origen y página.

CA1, «Completar con …»: GET y POST `/api/forms/catalogs/{id}/lookup` consumen IFormCatalogLookupService y exigen `CatalogsManage` (atributo de clase), con no-store. GET dice qué padrón completa el catálogo y el campo de la CUIT, con la política de lectura; POST recibe el cuerpo cerrado `{ taxId }` de hasta 8 KiB, con la de escritura, y responde 200 también cuando el padrón no pudo (`failureCode` y `failureMessage`). Sin un padrón para ese catálogo, o sin su campo activo, 404 `Forms.Catalog.LookupUnavailable`. No guarda nada, y las llamadas al padrón las limita el módulo que lo aporta.

H4: POST catalogs/{id}/proposal/approve y /discard consumen IFormCatalogProposalService con Read+Manage, cuerpo cerrado y expectedRevision. Aprobar recibe los textos editados, Filters y sampleUserId de la vista previa; no recibe actor, estado ni procedencia. Devuelven 200 o conflicto 409; el detalle incluye proposal nullable con evidencia original y decisión real, sin respuestas del proveedor.

### El flujo de una respuesta

`FormWorkflowsController` (`api/forms/records/{recordId}`) admite en la clase «Ver respuestas» o «Completar formularios». `retry`, `recipients/{userId}/retry`, `review` y `consult` suman en la acción «Ver respuestas» y `formrules.manage`; `POST actions/{runId}/confirmation` («¿La emito?») no suma nada, porque la decide quien cargó la respuesta, también con sólo «Completar formularios». Cuerpos cerrados; el de `confirmation`, hasta 8 KiB. El detalle está en [reglas, efectos, aprobaciones y PDF](../../../../../docs/features/formularios-borde-http.md#reglas-efectos-aprobaciones-y-pdf).

El tablero (`FormDashboardsController`, DOC7-A) sigue las reglas de la planilla: está en [respuestas, archivos y corrección](../../../../../docs/features/formularios-borde-http.md#respuestas-archivos-y-corrección).

«Documentos recibidos» (`FormReceivedDocumentsController`, `api/forms/documents`, DOC5-A): «Ver documentos recibidos» en la clase, para la lista (POST `search`, porque sus filtros son privados; búsqueda por los números leídos, conteos por estado y las opciones de «Formulario» y «Mandó», cada una contada con los demás filtros, hasta 100 con la elegida siempre adentro y `formsTruncated`/`sendersTruncated` si cortan), los conteos del menú, el detalle (el archivo con `stored`; las opciones de una elección, cada una con `group`, sólo para quien revisa y del formulario actual de un documento propuesto, si no una lista vacía, y `optionsUnavailable` cuando una de catálogo no trae ninguna) y el archivo (no-store y nosniff; 409 sin su copia y 404 `FileNotStored` si se guardó sin ella, tarea 8); «Revisar documentos» suma en descartar y reabrir (`expectedRevision`, 409), cambiar de formulario (202 y después GET `extraction?requestId=`; 403 o 500 cuando el motor se niega o falla, y si se niega al leerla la descarta), buscar las opciones de una elección (POST `{id}/options` con `fieldId` y `search` en el cuerpo: hasta 50 y `truncated`, con la política de lectura; 404 si no es una elección de catálogo del formulario del documento y 409 `StateConflict` una vez pasado o descartado) y pasar al formulario (`records`, idempotente por `requestId`). Mientras su persona todavía puede completarlo, descartar, cambiar de formulario y pasar dan 409 `Forms.ReceivedDocument.AwaitingSender`. Cuerpos cerrados de hasta 512 KiB y las políticas de lectura, escritura y archivos de las respuestas.

«Cómo viene leyendo» (`FormDocumentReadingController`, DOC1): GET `definitions/{id}/document-reading` con «Ver formularios», la política de lectura y no-store; las correcciones las suma el servicio sólo con «Ver respuestas». El detalle está en [formularios](../../../../../docs/features/formularios.md#lectura-de-documentos-y-validaciones-fijas--doc1-2026-11-11).

## Referencias

- [Reglas comunes de los controllers](../../../Controllers/AGENTS.md).
- [Inventario de rutas del módulo](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormsTestModule.cs) y [sus pruebas](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/AGENTS.md).
