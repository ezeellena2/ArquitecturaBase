# Controllers: rutas de Formularios

Un controller por familia de rutas, con las interfaces de servicio de esa familia. Acá están las rutas, los permisos, las políticas, los límites, los encabezados y las respuestas de borrador, publicación, vista previa, baja, eliminación y catálogos. La vista previa de destinatarios y la carga desde la app se describen con sus contratos en [Contracts](../Contracts/AGENTS.md); las demás familias, en el [detalle por tema](../../../../../docs/features/formularios-borde-http.md).

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

E1-A: POST catalogs/{id}/import-previews e /imports reciben un contrato FromForm y exigen Read+Manage; el archivo vive sólo durante la llamada y se cierra al terminar. El límite HTTP es 10 MiB más 64 KiB de envoltorio; el lector conserva el tope de archivo de 10 MiB. Ambas rutas y GET catalogs/{id}/export usan forms-export, sin cola, y no-store; el XLSX suma nosniff. Export y template sólo exigen Read. PUT admite SortAlphabetically sin movimientos simultáneos.

Planillas de la IA (Etapa 2): POST catalogs/{id}/assistant-import-previews y /assistant-imports importan desde la copia de la planilla de una propuesta, en el mismo controller, con forms-export, no-store y Read+Manage. Reciben JSON cerrado (512 KiB): `requestId`, `proposalId` y `expectedRevision`, y al aplicar además `sheetSha256` y las confirmaciones; responden la misma vista previa (`FormCatalogImportPreviewResponse`) y el mismo recibo que subir un archivo. GET catalogs/{id}/assistant-import, sólo con Read, da la importación pendiente (`FormCatalogAssistantImportResponse`: `pending` null si no hay nada, nunca 404 por eso), así el detalle del catálogo no cambia de forma.

K4 Filters: `GET /api/forms/catalog-filter-options` entrega columnas/roles, disponibilidad de la vista y muestra paginada/ordenable sólo con `CatalogsManage`; `POST /api/forms/catalog-previews` exige Read+Manage y no guarda. Alta/edición usan `POST /api/forms/catalogs/filters` y `PUT /api/forms/catalogs/{id}/filters`; detalle sigue en `GET /api/forms/catalogs/{id}`. Sólo la vista previa recibe `sampleUserId`. `GET /api/forms/catalogs` usa `state=Active|Inactive|Unavailable`; responde `filterCounts.byKind` de todo el conjunto filtrado por búsqueda/estado, independiente de origen y página.

H4: POST catalogs/{id}/proposal/approve y /discard consumen IFormCatalogProposalService con Read+Manage, cuerpo cerrado y expectedRevision. Aprobar recibe los textos editados, Filters y sampleUserId de la vista previa; no recibe actor, estado ni procedencia. Devuelven 200 o conflicto 409; el detalle incluye proposal nullable con evidencia original y decisión real, sin respuestas del proveedor.

## Referencias

- [Reglas comunes de los controllers](../../../Controllers/AGENTS.md).
- [Inventario de rutas del módulo](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormsTestModule.cs) y [sus pruebas](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/AGENTS.md).
