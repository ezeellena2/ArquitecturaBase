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
