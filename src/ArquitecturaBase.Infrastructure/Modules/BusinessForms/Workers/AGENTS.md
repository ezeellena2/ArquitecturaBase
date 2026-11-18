# Workers: publicación, acciones, purga y catálogos de un módulo

Hosts en segundo plano del módulo. Programan y abren scopes; la reserva, la decisión de estados y el guardado pertenecen a los servicios de Application, y el efecto externo a los canales neutrales.

- `FormPublicationBackgroundService` (`BusinessForms:PublicationWorker:ProcessInBackground`): cada 2 s lista hasta 20 operaciones vencidas, reserva la primera disponible y la lleva por reserva → despacho a los canales (`IFormPublicationChannelDispatchService`) → completado. Una vuelta avanza una operación.
- `FormActionBackgroundService` (`BusinessForms:ActionWorker:ProcessInBackground`): cada 2 s toma el lote de hasta 100 runs listos y los procesa todos, cada uno con reserva, efecto (`IFormActionEffectService`) y confirmación.
- `FormCatalogFeedBackgroundService` (`BusinessForms:CatalogFeedWorker:ProcessInBackground`, cada `PollInterval`, 5 minutos): lista los catálogos `External` que tocan (`IFormCatalogFeedService`: sin leer, leídos hace más de un día o fallados hace más de 15 minutos) y, cada uno en su scope, reserva, lee el origen fuera de toda transacción y aplica. Una falla de uno no frena a los demás y su motivo queda en el catálogo.
- `FormDraftRetentionBackgroundService`: cada 15 minutos llama a `IFormDraftRetentionService`, que vence comprobantes de guardado en lotes de 100 con entidades seguidas; guardar un borrador también purga un lote.
- `FormReceivedDocumentRetentionBackgroundService` (F40): al arrancar y cada hora purga los documentos recibidos vencidos, lotes seguidos mientras salgan llenos, cada uno en su transacción y con tope de tiempo.
- `FormDocumentRereadBackgroundService` (`BusinessForms:DocumentRereadWorker`, cada `PollInterval`, 5 s; DOC6): aplica las relecturas terminadas, reserva por una vuelta los documentos pendientes de cada pedido cuyo formulario se publicó con la lectura (`IFormDocumentRereadService`, bajo el lock del pedido), pide la extracción de cada uno y anota qué pasó, cada paso en su scope; lo que el motor no pudo tomar o un paso fallido sigue en otra vuelta, y un documento que tira tres veces seguidas (`MaxThrows`) sale de los pendientes, con su código, para no retener su pedido ni los demás de esa persona.

## Al modificar

- Cada paso usa un scope nuevo: ningún `DbContext`, lock ni transacción queda vivo durante el despacho o el efecto. El worker no arma snapshots ni pedidos de canal; recibe lo que devuelve la reserva.
- La reserva de publicaciones y acciones toma el lock global de definiciones y fija lease y fence en la fila; sólo el dueño vigente completa. No agregues otro mecanismo de exclusión ni un lock nuevo. El feed reserva en el catálogo, con el lock de su clave y después el del catálogo.
- Una falla técnica de un candidato se registra con el tipo de excepción y no saltea los siguientes; lo no confirmado queda para la recuperación durable, que lo inspecciona sin repetir un efecto incierto.

- F18: los workers de acciones y de publicación continúan la traza guardada al crear el run o la operación (`TraceParent`, de `FormsTraceInterceptor`): abren `forms.action` o `forms.publication` con `FormsTelemetry` al reservar, que devuelve el contexto. Las etiquetas llevan sólo ids y el tipo de acción.
- Los lotes salen ordenados por `NextCheckAtUtc` o `AcceptedAtUtc` con desempate estable. Un run es elegible sólo cuando terminó la acción anterior de su misma regla (Completed o NotApplicable); las dependencias no cruzan reglas.
- Un efecto en duda por un error local sigue Running y se consulta solo, con espera creciente, hasta ocho veces (`AutoConsultDelay`, F2); después queda Uncertain, que no se sondea solo: una consulta o reintento explícito lo pasa a Running con el mismo RunId. Un efecto conocido pendiente conserva su próxima consulta.
- Sin canal disponible la publicación queda Uncertain; abierta, se vuelve a mirar a un cuarto de su edad (30 s a 30 min) con los mismos IDs (F3); la activación la decide Application con la base activa, el fence y los permisos actuales.
- `ProcessNextAsync(id)` permite que los tests avancen una operación concreta con el worker apagado; no lo uses como atajo productivo.

## Referencias

- [Publicación y preview](../../../../../docs/features/formularios.md#publicación-y-preview--implementación-2b-2026-10-10) y [motor de acciones y aprobación](../../../../../docs/features/formularios.md#motor-de-acciones-y-aprobación--etapa-4-g5-y-g3g4).
- [Robustez proporcional al efecto](../../../../../docs/architecture/backend.md#robustez-proporcional-al-efecto) y [servicios de fondo](../../../../../docs/architecture/backend.md#reglas-de-ubicación-y-acceso).
- Pruebas: [worker de publicación](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormPublicationWorkerTests.cs), [logs](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/PublicationWorkerLoggingTests.cs), [motor de acciones](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormActionBackgroundServiceTests.cs) y [recuperación](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormActionRecoveryIntegrationTests.cs).
