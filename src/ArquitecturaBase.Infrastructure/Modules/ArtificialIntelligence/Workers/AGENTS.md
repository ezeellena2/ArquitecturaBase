# Asistente: coordinación técnica

El coordinador organiza claim, preparación, reserva de paso, HTTP/herramientas y cierre.
Cada unidad obtiene y libera un scope nuevo; no conserva DbContext ni llama al proveedor dentro
de una transacción. Los escritores de Application son los únicos dueños de la UoW.

El deadline del intento no se renueva. Un Running abandonado se reconcilia sin repetir generación.
Si ya hay un resultado, el reintento técnico se limita a confirmar ese mismo cierre con su fence.
Cancelación y shutdown son cooperativos; una salida posible sin cierre queda durable para reconciliar.

Los schedulers usan TimeProvider y tokens de parada. Logs y métricas contienen estados, códigos,
duraciones e IDs operativos; nunca preguntas, respuestas, resultados ni credenciales.

Hereda [el módulo](../AGENTS.md) y [la arquitectura](../../../../../docs/architecture/backend.md).
Verificá carreras y recuperación con PostgreSQL y hosts independientes; handlers falsos para HTTP.
