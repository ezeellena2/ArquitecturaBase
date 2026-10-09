# Emails: render, cola y entrega

Implementa templates localizados, cola y worker de correo y adaptadores de entrega. Las reglas de emisión e invitación pertenecen a Application.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../docs/features/identidad.md).

## Al modificar

- Los templates y sus recursos se mantienen en los dos idiomas; escapá los datos insertados en HTML.
- La cola productiva persiste cifrada en la transacción del llamador. El registro usa el store scoped; el constructor en memoria sólo sirve para tests aislados. Nunca enviar antes del commit ni repetir una reserva incierta.
- El worker resuelve dependencias scoped por operación; los secretos SMTP vienen de configuración externa.
- Probá qué ocurre si no se encola o falla el envío sin filtrar dirección, código o enlace en logs.

## Verificación y ejemplos

- [Colas transaccionales](../../../docs/architecture/backend.md).
- [Pruebas de correo](../../../tests/ArquitecturaBase.Api.IntegrationTests/Emails).
