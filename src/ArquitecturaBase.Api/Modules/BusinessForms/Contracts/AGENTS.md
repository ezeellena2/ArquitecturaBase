# Contracts: entrada HTTP de Formularios

Cuerpos `*HttpRequest`, queries `*Query` y respuestas HTTP propias (`FormDraftHttpResponses.cs`, `FormPublicationReceiptHttpResponse.cs`), con los mapeos que comparten varias rutas: `FormDraftRequestMapping` (borrador, chequeo, aceptación y destinatarios), `FormEntryRequestMapping` (carga y corrección) y `FormRecordsRequestMapping` (respuestas y exportación). Esta guía detalla su forma y, con sus rutas, la vista previa de destinatarios y la carga desde la app. Las demás rutas están en [Controllers](../Controllers/AGENTS.md); el [detalle por tema](../../../../../docs/features/formularios-borde-http.md) describe juntos las rutas y los cuerpos de sus familias.

## Al modificar

### Borrador y publicación

- D5 humana adapta directamente los seis DTOs al nombre mapa es/en; el front actualiza sus guards cerrados. Sin variante legada/versionado porque el módulo no estaba desplegado. Aceptación/status adoptan el contrato revisado; preview tiene consumidor propio de lectura.

FormPreviewQuery exige culture es/en en Application y devuelve400 por campo al faltar/no ser admitida. Detalle agrega publication seguro/capabilities; nunca expone UUID de recuperación ajeno. No-store también en los errores de preview.

El borrador cerrado admite files (minFiles, maxFiles, photoSource) y, sólo en multiChoice, selection (minSelected, maxSelected); el borrador suma `sections` (`start` y `title`), los títulos de sección. Guardado, chequeo y aceptación comparten `Contracts/FormDraftRequestMapping`, que preserva también los valores ausentes para el validador; sin rutas ni permisos nuevos para autoría. Selection conserva su omisión al null en los DTO sellados.

Etapa 5, editor 03: create/update de borrador aceptan offerWhen escalar nullable; Definition lo devuelve también en commit/recuperación. No agrega rutas ni permisos. Chequeo y aceptación mantienen su cuerpo cerrado sin offerWhen: este texto se guarda con el borrador y no forma parte de una publicación.

E2-3: Source agrega Match/Presentation en el mapeo único, con contratos cerrados y redacción. GET recipient-samples y POST definitions/{id}/recipient-previews consumen IFormRecipientPreviewService, y POST recipient-maximums IFormRecipientMaximumService, en el mismo controller con Read+Manage+CatalogsRead (atributo de clase), no-store y políticas existentes (los POST, la de escritura); sólo la vista previa recibe persona, filtros y cultura, y recipient-maximums recibe sólo `source` en un cuerpo cerrado y devuelve `maximum` y `maximumLabel`. Publicación devuelve warnings siempre, separados de issues.

### Catálogos

E1-B: el alta Manual admite `valueType` opcional (`Text` por defecto, `Integer`, `Decimal` o `Date`); PUT no lo admite y rechaza desconocidos. Cada ítem admite `value` nullable. Las respuestas de catálogo siempre llevan `valueType` y las de ítem `value`, incluso null; los errores de normalización señalan `items[index].value`. El controller sólo mapea, sin interpretar valores.

Los requests de ítems no reciben `Position`; el servidor asigna altas al final. PUT acepta una secuencia opcional `moves: [{ itemId, direction: "up" | "down" }]`, sin escrituras por flecha y bajo el mismo Guardar/revisión. También acepta `deleteItemIds` (identidades de opciones que nada usa); una usada da 409 `Forms.Catalog.ItemInUse` con `uses` en `deleteItemIds[i]`.

### Carga desde la app

Carga desde la app: `api/forms/entries/{definitionId}` consume IFormEntryService con formrecords.create. GET da la versión activa para cargar; GET `versions/{versionId}/questions/{fieldId}/options`, una página de hasta 50 opciones con búsqueda y los filtros de quien carga; POST recibe un multipart (`requestId`, `versionId`, `answers` en JSON y `files` con su `fileFieldIds`, uno por archivo) con límite de 101 MiB. `FormEntryRequestMapping` deja null un JSON inválido o archivos sin su pregunta, para que el validador responda 400. El bloque de renglones va en `answers` como `{ fieldId, rows: [[{ fieldId, value }, …], …] }` (`FormEntryAnswerHttpItem.Rows`); una respuesta o un ítem nulos llegan nulos al validador. Elegir primero por un campo (LD1-A): GET `versions/{versionId}/questions/{fieldId}/groups` (en la carga y en la corrección) da los valores con los mismos `answers[...]`, y las opciones suman `group` o `withoutGroup`; el borrador suma `source.groupBy`. No-store y las políticas de lectura y escritura. La búsqueda de respuestas, sus conteos, la navegación y el Excel suman `enteredInApp`: true sólo lo cargado en la app y false lo que llegó por un canal.

## Referencias

- [Reglas comunes de los contratos](../../../Contracts/AGENTS.md) y [ADR 0002](../../../../../docs/decisions/0002-contratos-http.md).
- [Pruebas del módulo](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/AGENTS.md).
