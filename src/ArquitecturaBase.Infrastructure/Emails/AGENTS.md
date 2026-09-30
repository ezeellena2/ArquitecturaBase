# Emails: render, cola y entrega

Implementa templates localizados, cola y worker de correo y adaptadores de entrega. Las reglas de emisión e invitación pertenecen a Application.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../docs/features/identidad.md).

## Al modificar

- Los templates y sus recursos se mantienen en los dos idiomas; escapá los datos insertados en HTML.
- La cola actual es de proceso y no es Redis ni un outbox durable. No prometer entrega persistente por agregar caché.
- El worker resuelve dependencias scoped por operación; los secretos SMTP vienen de configuración externa.
- Probá qué ocurre si no se encola o falla el envío sin filtrar dirección, código o enlace en logs.

## Verificación y ejemplos

- [Colas en memoria](../../../docs/architecture/backend.md).
- [Pruebas de correo](../../../tests/ArquitecturaBase.Api.IntegrationTests/Emails).
