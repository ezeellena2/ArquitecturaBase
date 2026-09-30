# Roles: área de referencia

`RoleService` coordina lectura y cambios de roles y permisos. Es el patrón de la plantilla para un área MVC nueva.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/administracion.md`](../../../../docs/features/administracion.md).
Roles es además el área de referencia para copiar ([ADR 0004](../../../../docs/decisions/0004-roles-como-area-de-referencia.md)).

## Al modificar

- Copiá el patrón de contratos, validación, mapeos y límite; el catálogo completo y el listado paginado son dos consumidores distintos.
- Los roles del sistema tienen restricciones propias y un rol asignado no se borra; conservá los errores y sus datos de respuesta.
- La modificación confirma primero y luego invalida permisos. La revisión de Identity identifica la versión compartida en Redis.

## Verificación y ejemplos

- [Servicio a copiar](RoleService.cs).
- [Pruebas del servicio](../../../../tests/ArquitecturaBase.Application.UnitTests/Services/Roles).
- [Receta de una funcionalidad](../../../../docs/guides/agregar-un-area.md).
