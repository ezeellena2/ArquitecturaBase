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

Catálogos manuales: GET/POST `/api/forms/catalogs`, GET/PUT `/api/forms/catalogs/{id}` y GET `/api/forms/catalogs/{id}/items`; Read en lectura, Manage adicional en alta/edición. Mapear queries HTTP a mano. `expectedRevision` es obligatorio para PUT y los conflictos no guardan parcialmente.

Los requests de ítems no reciben `Position`; el servidor asigna altas al final. PUT acepta una secuencia opcional `moves: [{ itemId, direction: "up" | "down" }]`, sin escrituras por flecha y bajo el mismo Guardar/revisión.

K4 Filters: `GET /api/forms/catalog-filter-options` entrega columnas/roles, disponibilidad de la vista y muestra paginada/ordenable sólo con `CatalogsManage`; `POST /api/forms/catalog-previews` exige Read+Manage y no guarda. Alta/edición usan `POST /api/forms/catalogs/filters` y `PUT /api/forms/catalogs/{id}/filters`; detalle sigue en `GET /api/forms/catalogs/{id}`. Sólo la vista previa recibe `sampleUserId`. `GET /api/forms/catalogs` usa `state=Active|Inactive|Unavailable`; responde `filterCounts.byKind` de todo el conjunto filtrado por búsqueda/estado, independiente de origen y página.

F1 extiende el contrato cerrado del borrador con `files: { minFiles, maxFiles, photoSource }`; los tres controllers de guardado/chequeo/aceptación mapean esa configuración. Sin rutas ni permisos nuevos para autoría.

F4: GET records/{recordId}/files/{fileId}/thumbnail y /content consumen IFormFileService y exigen formrecords.read. content recibe download opcional. No-store/nosniff también en errores; documentos como attachment y fotos en línea sólo JPEG/PNG. Inventario/OpenAPI binario y tests de 401/403/404; no loguear nombres ni bytes.
