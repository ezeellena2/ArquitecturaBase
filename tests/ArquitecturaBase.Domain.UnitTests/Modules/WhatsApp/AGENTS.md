# WhatsApp: tests de Domain.UnitTests

Prueba entidades y reglas puras del módulo, sin proveedores.

## Al modificar

- Probar default Manual, combinaciones válidas de propósito/evento y rechazo de metadata inválida; conservar transiciones, identidad y estados del historial.
- Para Flows, distinguir reserva (`Begin`) del inicio durable del efecto (`MarkEffectStarted`); probar retry sólo cuando no se envió y reconciliación si el resultado es incierto.
- Probar que el estado propio de cada FlowReply no hereda accidentalmente un resultado de otra respuesta repetida.

- Los tests siguen el dominio del módulo; no mover sus errores o entidades al núcleo.
- El módulo debe poder quitarse sin dejar referencias en pruebas del núcleo.

## Referencias

- [Reglas funcionales del módulo](../../../../docs/features/whatsapp.md).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).

Las salidas Assistant prueban identidad capturada sin operador, botones acotados e inmutables y el fence compartido con FormAction. Una reserva de otro origen o vencida no inicia el POST; la incertidumbre sólo admite una aceptación tardía, sin recrear el envío.

H6 verifica descriptor inicial sin reclasificar Media, identidad inmutable del pedido de audio, publicación idempotente del texto completo y estados terminales. La recuperación de adquisición requiere ambos límites de cinco fallas saturadas y quince minutos.
