# Workers: publicación, acciones y purga de formularios

Hosts en segundo plano del módulo. Programan y abren scopes; la reserva, la decisión de estados y el guardado pertenecen a los servicios de Application, y el efecto externo a los canales neutrales.

- `FormPublicationBackgroundService` (`BusinessForms:PublicationWorker:ProcessInBackground`): cada 2 s lista hasta 20 operaciones vencidas, reserva la primera disponible y la lleva por reserva → despacho a los canales (`IFormPublicationChannelDispatchService`) → completado. Una vuelta avanza una operación.
- `FormActionBackgroundService` (`BusinessForms:ActionWorker:ProcessInBackground`): cada 2 s toma el lote de hasta 100 runs listos y los procesa todos, cada uno con reserva, efecto (`IFormActionEffectService`) y confirmación.
- `FormDraftRetentionBackgroundService`: cada 15 minutos llama a `IFormDraftRetentionService`, que vence comprobantes de guardado en lotes de 100 con entidades seguidas; guardar un borrador también purga un lote.

## Al modificar

- Cada paso usa un scope nuevo: ningún `DbContext`, lock ni transacción queda vivo durante el despacho o el efecto. El worker no arma snapshots ni pedidos de canal; recibe lo que devuelve la reserva.
- La reserva toma el lock global de definiciones existente y fija lease y fence en la fila; sólo el dueño vigente completa. No agregues otro mecanismo de exclusión ni un lock nuevo.
- Una falla técnica de un candidato se registra con el tipo de excepción y no saltea los siguientes; lo no confirmado queda para la recuperación durable, que lo inspecciona sin repetir un efecto incierto.
- Los lotes salen ordenados por `NextCheckAtUtc` o `AcceptedAtUtc` con desempate estable. Un run es elegible sólo cuando terminó la acción anterior de su misma regla (Completed o NotApplicable); las dependencias no cruzan reglas.
- Uncertain no se sondea ni se reserva solo: una consulta o reintento explícito de Application lo pasa a Running con el mismo RunId y recién ahí entra al lote. Un efecto conocido pendiente conserva su próxima consulta.
- Si el canal no está disponible, la publicación queda Uncertain y se reintenta con los mismos IDs; la activación la decide Application con la base activa, el fence y los permisos actuales.
- `ProcessNextAsync(id)` permite que los tests avancen una operación concreta con el worker apagado; no lo uses como atajo productivo.
- Las pruebas de varios hosts, caídas y carreras se reservan a estos efectos externos, no a la configuración interna.

## Referencias

- [Publicación y preview](../../../../../docs/features/formularios.md#publicación-y-preview--implementación-2b-2026-10-10) y [motor de acciones y aprobación](../../../../../docs/features/formularios.md#motor-de-acciones-y-aprobación--etapa-4-g5-y-g3g4).
- [Robustez proporcional al efecto](../../../../../docs/architecture/backend.md#robustez-proporcional-al-efecto) y [servicios de fondo](../../../../../docs/architecture/backend.md#reglas-de-ubicación-y-acceso).
- Pruebas: [worker de publicación](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormPublicationWorkerTests.cs), [logs](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/PublicationWorkerLoggingTests.cs), [motor de acciones](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormActionBackgroundServiceTests.cs) y [recuperación](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormActionRecoveryIntegrationTests.cs).
