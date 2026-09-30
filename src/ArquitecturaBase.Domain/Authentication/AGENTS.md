# Authentication: códigos, enlaces y auditoría

Modela los hechos y las reglas puras del acceso: propósito y destino de códigos, consumos, intentos, enlaces de un solo uso y errores de cuenta.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../docs/features/identidad.md).
El enlace de un solo uso (`LoginLink`) es del núcleo y su regla está en `identidad.md`. Los códigos que se piden por un número, con su tope diario, son del módulo WhatsApp y siguen además [`docs/features/whatsapp.md`](../../../docs/features/whatsapp.md).

## Al modificar

- Los códigos distinguen ingreso de registro y de verificación de un destino; conservá esa separación al agregar un flujo.
- Las fechas llegan como datos UTC; el modelo no consulta un reloj ni un proveedor.
- Un error nuevo se declara acá con código estable y textos en ambos recursos de Application.

## Verificación y ejemplos

- [Pruebas de códigos y enlaces](../../../tests/ArquitecturaBase.Domain.UnitTests/Authentication).
