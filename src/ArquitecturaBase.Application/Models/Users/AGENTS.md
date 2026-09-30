# Users: pedidos y proyecciones

Define entradas funcionales, respuestas y filas de usuarios, perfil, invitaciones y filtros.

## Lectura obligatoria

Antes de tocar el listado de usuarios (`ListUsersRequest`, `UserFilterCounts`), leé [`docs/features/administracion.md`](../../../../docs/features/administracion.md).

## Al modificar

- Los campos de filtro y orden son contrato de lector, validador y URL del front; modificá sus consumidores juntos.
- Una cuenta puede no tener correo. Conservá nulabilidad, confirmaciones y formas de mostrar el número que entrega el backend.
- Los datos personales expuestos deben ser los necesarios para la operación; un `Row` no es una entidad de Identity.

## Verificación y ejemplos

- [Pruebas de pedidos y servicios](../../../../tests/ArquitecturaBase.Application.UnitTests/Services/Users).
- [Modelos y paginado](../../../../docs/architecture/backend.md).
