# WhatsApp: tests de Domain.UnitTests

Prueba entidades y reglas puras del módulo, sin proveedores.

## Al modificar

- Probar default Manual, combinaciones válidas de propósito/evento y rechazo de metadata inválida; conservar transiciones, identidad y estados del historial.
- Para Flows, distinguir reserva (`Begin`) del inicio durable del efecto (`MarkEffectStarted`); probar retry sólo cuando no se envió y reconciliación si el resultado es incierto.

- Los tests siguen el dominio del módulo; no mover sus errores o entidades al núcleo.
- El módulo debe poder quitarse sin dejar referencias en pruebas del núcleo.

## Referencias

- [Reglas funcionales del módulo](../../../../docs/features/whatsapp.md).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).
