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
