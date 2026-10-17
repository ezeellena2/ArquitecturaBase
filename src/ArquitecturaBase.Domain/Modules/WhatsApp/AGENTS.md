# WhatsApp: dominio del módulo

Define contactos, mensajes y errores del módulo. Es independiente de Meta, HTTP, EF y las implementaciones del núcleo.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).

## Al modificar

- Nuevos contactos arrancan Manual. Purpose distingue Business/Access/Unclassified; solo Access lleva AccessEventKind. No inferir propósito por el cuerpo ni reclasificar historia por frases.

- Chat usa modo/revisión del contacto, secuencia entrante, lectura por operador, salida Pending/Sending/Accepted/Failed/Uncertain/Reviewed y receipts tempranos. Reviewed habilita atención sin afirmar entrega ni reenviar.

- Las reglas puras y transiciones se prueban en el dominio del módulo.
- Conservá consentimiento, identidad de mensajes y los estados que permiten idempotencia y entrega.
- Una dependencia del núcleo puede ser compartida; el núcleo no depende de estas entidades.

## Verificación y ejemplos

- [Pruebas del dominio](../../../../tests/ArquitecturaBase.Domain.UnitTests/Modules/WhatsApp).
- [Cómo quitar el módulo](../../../../docs/guides/quitar-whatsapp.md).

FlowErrors declara errores puros de representabilidad/codec del canal. No contiene JSON del proveedor ni referencias a BusinessForms; también declara errores seguros de publicación. Sus códigos se traducen en Errors.resx/en.

WhatsAppFlowOperation/Binding modelan estados/fases/lease/fence del módulo, sin importar BusinessForms. Begin reserva el paso; MarkEffectStarted confirma el efecto durable inmediatamente antes del proveedor. Sólo una falla que demuestra que no se envió puede limpiar InFlight y reintentar. IDs confirmados sobreviven fallos, fence tardío no completa y Published requiere verificación. Incertidumbre parcial tiene prioridad sobre fallo; revocación no transforma un efecto desconocido en certeza.

LinkRevision detecta cambios efectivos de vínculo, incluido unlink/relink a la misma cuenta. No incrementarla por replay o entrantes; el consumidor compara contexto además de UserId.

Una respuesta Flow completa sus estados de sesión y mensaje; el mensaje conserva su propio resultado para que una repetición alterada no herede el registro de la respuesta original. Pending/Completed/Rejected/Unrecognized son estados internos, sin cuerpos ni valores externos.

G6: las salidas FormAction fijan RunId como RequestId, destinatario, contacto, número y revisión del vínculo, sin operador. Reserva y comienzo del POST de mensaje son fases distintas con token propio: vencer durante upload vuelve a Pending y conserva el mediaId; después del comienzo queda Uncertain. Sólo Failed confirmado admite retry; el retry descarta un mediaId rechazado y Accepted jamás se repite. El historial conserva Origin para aplicar privacidad de valores, incluso después de recargar.

Un FormAction incierto puede recibir revisión humana con decisión, actor y fecha: salió queda Reviewed, no salió queda Failed reintentable. Ninguna revisión inventa un wamid ni un receipt; una aceptación tardía puede confirmar también el fallo revisado, sin ampliar transiciones de fallos manuales.
Review y Retry comparten LastFormActionControlRevision: un control igual o anterior no actúa sobre otro intento del mismo run.

WhatsAppDeliveryReceipt acumula DeliveredAtUtc y ReadAtUtc reales antes del fence de último estado, incluyendo eventos atrasados. Preserva primero la fecha conocida de una fila legada sin columnas pobladas; nunca deduce entrega desde lectura ni desde una revisión humana.

Etapa5: los modos vigentes son Manual y Assistant; Manual sigue siendo el default. ModeRevision sólo avanza al cambiar. La migración de datos convierte Bot previo a Manual, sin activar el asistente ni reconstruir la historia. ConfigureAssistant es el permiso nuevo del catálogo; Send permite tomar Manual.

Las salidas Assistant fijan destinatario, número, vínculo y ModeRevision sin operador; Text, Flow y QuickReplies comparten la cola. Las respuestas rápidas conservan IDs y títulos cerrados al encolar. Assistant y FormAction comparten las transiciones de reserva/POST con tokens propios y origen obligatorio: antes del POST vencido vuelve a Pending, después queda Uncertain. No hay retry automático de Assistant incierto.

H6: Audio es un tipo nuevo sólo para entrantes nuevos autenticados; Media histórico conserva su tipo. El mensaje fija descriptor, RequestId y actor una sola vez, guarda texto sólo al completar NormalizeAudio y conserva estados inciertos terminales. La adquisición recuperable satura cinco fallas pero sólo termina al cumplir también quince minutos. El contenido vence desde la recepción del mensaje.
