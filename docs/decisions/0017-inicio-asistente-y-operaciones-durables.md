# ADR 0017: Inicio con assistant-ui y operaciones durables

## Estado

Aceptada como dirección de implementación, 2026-10-04, por pedido explícito del usuario.

## Contexto

Inicio autenticado necesita consultar datos actuales autorizados mediante la conexión LLM guardada. Una llamada facturable no puede repetirse por recarga, pérdida de red o reinicio del host. La comparación visual ya eligió assistant-ui.

## Decisión

Componer Inicio desde app y adaptar @assistant-ui/react mediante ExternalStoreRuntime a los turnos confirmados del servidor. MVC acepta una operación con UUID y PostgreSQL conserva conversación privada, turno, recibo, reserva y resultado; un coordinador de Infrastructure usa scopes separados y HTTP fuera de transacciones para herramientas tipadas y respuesta terminal validada.

## Consecuencias

Se mantienen servicios Application, puertos neutrales, Result y una UoW por escritura. GET nunca inicia generación. Fencing, revisión de conexión, permisos actuales, cuotas distribuidas y recibos impiden reenvíos accidentales. Contenido cifrado usa propósitos independientes del anillo externo. Running con posible envío perdido pasa a Uncertain y exige decisión explícita para un intento nuevo.

Las herramientas conservan IDs funcionales con puntos; cada adaptador los traduce a nombres admitidos por su protocolo y rechaza colisiones. Los resultados y fuentes se arman desde datos del servidor. La autorización no depende del prompt.

Streaming de texto, escrituras de negocio y MCP externo quedan fuera. Los límites y retención iniciales siguen como propuestas del piloto hasta su aprobación/calibración. Ningún perfil de modelo viene habilitado por inferir su nombre; se requiere una combinación explícita y comprobada.

## Alternativas descartadas

- Estado de conversación sólo en la biblioteca o memoria del worker: pierde recuperación y coordinación entre réplicas.
- Reintentar HTTP facturable automáticamente: puede duplicar consumo y respuestas.
- SQL/URLs genéricos elegidos por el modelo: incumplen autorización y alcance tipado.
- Una transacción abierta durante el proveedor: retiene locks y mezcla efectos externos con rollback.
