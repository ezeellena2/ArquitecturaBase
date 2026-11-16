# Asistente: coordinación técnica

El coordinador organiza claim, preparación, reserva de paso, HTTP/herramientas y cierre.
Cada unidad obtiene y libera un scope nuevo; no conserva DbContext ni llama al proveedor dentro
de una transacción. Los escritores de Application son los únicos dueños de la UoW.

El deadline del intento no se renueva. Un Running abandonado se reconcilia sin repetir generación.
Si ya hay un resultado, el reintento técnico se limita a confirmar ese mismo cierre con su fence.
Cancelación y shutdown son cooperativos; una salida posible sin cierre queda durable para reconciliar.
ProviderUnavailable sale sólo de la llamada al proveedor; una excepción fuera de ella es ExecutionFailed (I5).
Una falla del proveedor se registra con su código, el estado HTTP, el turno y el proveedor; `LlmProviderFailures` la clasifica (I6).
Un valor nuevo del sello o del proveedor se escribe un despliegue después del que ya lo lee ([despliegue](../../../../../docs/guides/despliegue.md), I4).
I10: `AiAssistantTracing` abre una actividad por turno que continúa el `TraceParent` del claim (ilegible: se ignora) y envuelve
capacidades y proveedor; la marca lo guardado: si el turno no terminó completo, error con su código, también si lo cerró el servicio
(límite de pasos, acceso, salida inválida); sin terminar, nada. Etiquetas: turno, canal, propósito, proveedor y capacidades con versión,
nunca contenido. El Id del turno va en el scope de los logs del worker. El sondeo de cancelación corre fuera de la actividad.
I17: sin un anillo que coincida con `AiKeyRingControls` (o mientras no se pudo verificar), `ProcessNextAsync` no toma turnos.
I12: el worker frena con `ApplicationStopping` y vacía `HostOptions.ShutdownTimeout` menos el cierre (`DrainTime`). `ProcessNextAsync(stop, drain)`: con el stop no toma turnos y devuelve a pendiente (`ReleaseAsync`; sólo lo devuelto cuenta en `ai.assistant.releases`) lo que no reservó paso; el fin del vaciado corta con `Ai.Assistant.Interrupted`.

Los schedulers usan TimeProvider y tokens de parada. Logs y métricas contienen estados, códigos,
duraciones e IDs operativos; nunca preguntas, respuestas, resultados ni credenciales.

Hereda [el módulo](../AGENTS.md) y [la arquitectura](../../../../../docs/architecture/backend.md).
Verificá carreras y recuperación con PostgreSQL y hosts independientes; handlers falsos para HTTP.

Answer y Proposal son terminales del mismo recorrido. El coordinador usa IsComplete y confirma el resultado ya obtenido con token independiente/acotado, sin repetir proveedor ni capacidades. No agregar un scheduler para un canal o tipo de resultado nuevo.
