# 0011: separar acceso al sistema y atención por WhatsApp

## Estado

Aceptada, 2026-10-02, en el alcance autorizado del refactor.

## Contexto

La instalación comparte un WhatsApp por negocio. El bot respondía a cualquier mensaje de
una cuenta con un enlace secreto y podía crear cuentas y confirmar teléfonos. El modo Manual
acoplaba atención y acceso, y la invitación dependía de ese bot.

## Decisión

La atención es Manual por defecto. Los códigos se solicitan y verifican desde la web,
independientemente del modo del chat. El procesamiento entrante solo clasifica mensajes y
responde a identificadores estructurados conocidos con orientación pública; no modifica
identidad ni emite enlaces secretos. Se retira el bot anterior y se registra propósito y tipo
de evento en el historial.

## Consecuencias

- WANT_TO_ENTER y HAVE_ACCOUNT orientan a `/login`; CREATE_ACCOUNT, a `/registro`;
  DID_NOT_REQUEST_CODE no recibe respuesta. Se conservan los identificadores ya enviados.
- La invitación agrega el pedido de código en la web. Consentimiento, plantilla y estado de
  entrega mantienen sus reglas; la configuración externa se verifica con la persona responsable.
- Propósito/evento y previews son aditivos. Bot/Manual y rutas HTTP se conservan; Bot queda
  sin automatización configurada. No hay un cambio de contrato que active el ADR 0005.
- Una migración nueva convierte Bot a Manual y aumenta revisión. Solo salidas históricas
  con autor operador se clasifican Business; las demás son Unclassified. No se reescribe historia.
- Se conserva el canje de enlaces pendientes del núcleo, junto con locks, invalidación y sesión.
  La cola de acceso sigue en memoria; la salida manual sigue durable y coordinada por Redis.
- El detalle y la evidencia viven en [WhatsApp](../features/whatsapp.md). Tests simulados no
  acreditan el recorrido con teléfono real.

## Alternativas descartadas

- Silenciar el bot únicamente en Manual: impediría el acceso y mantendría el acoplamiento.
- Inferir ingreso de frases: confundiría consultas del negocio con autenticación.
- Mantener dos emisores o políticas equivalentes: duplicaría identidad y responsabilidades.
- Retirar rutas de enlaces o el enum Bot: rompería consumidores y enlaces pendientes sin necesidad.
- Agregar LLM, multitenancy o conexiones por operador: están fuera de esta plantilla y entrega.

## Enmienda del 2026-10-03

[ADR 0015](0015-colas-transaccionales-cifradas.md) reemplaza la cola de acceso en memoria por una cola transaccional cifrada. Se conserva la separación funcional entre acceso y atención y el circuito de revisión de la salida manual.
