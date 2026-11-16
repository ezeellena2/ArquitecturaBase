# Roles: área de referencia

`RoleService` coordina lectura y cambios de roles y permisos. Es el patrón de la plantilla para un área MVC nueva.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/administracion.md`](../../../../docs/features/administracion.md).
Roles es además el área de referencia para copiar ([ADR 0004](../../../../docs/decisions/0004-roles-como-area-de-referencia.md)).

## Al modificar

- Copiá el patrón de contratos, validación, mapeos y límite; el catálogo completo y el listado paginado son dos consumidores distintos.
- Los roles del sistema tienen restricciones propias y un rol asignado no se borra; conservá los errores y sus datos de respuesta.
- Quien pide sólo da o quita los permisos que tiene, y los de User sólo los cambia Admin: `PermissionDelegationGuard` (que también usa `UserGuard`) lee sus permisos de la base adentro del límite, no de lo que `IPermissionService` memorizó en el pedido.
- El alta, la edición y el borrado dejan su historia con `AdministrativeChangeRecorder` al final del trabajo, en el mismo límite y sólo si cambió algo; el borrado, con los permisos que tenía (N13, [administración](../../../../docs/features/administracion.md)).
- La edición y el borrado bloquean la fila del rol antes de leerlo; la edición compara `ExpectedRevision` con `Revision` y responde 409 si cambió (N2). El borrado choca así con una asignación en curso ([administración](../../../../docs/features/administracion.md)).
- Los permisos no tienen caché que invalidar ([ADR 0032](../../../../docs/decisions/0032-sin-redis-la-app-sigue.md)): lo confirmado vale desde el pedido siguiente, que los lee de la base.

## Verificación y ejemplos

- [Servicio a copiar](RoleService.cs).
- [Pruebas del servicio](../../../../tests/ArquitecturaBase.Application.UnitTests/Services/Roles).
- [Receta de una funcionalidad](../../../../docs/guides/agregar-un-area.md).
