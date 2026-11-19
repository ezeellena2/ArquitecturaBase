# Authorization: catálogo de permisos

Define permisos, roles del sistema y errores de roles. El catálogo es compartido por servicios, seed, autorización HTTP y traducciones.

## Al modificar

- Agregá un permiso a su grupo y a `Permissions.All`; completá sus textos en ambos recursos de Application. Si no sirve sin el «Ver» de su área, declará lo que incluye en `Permissions.Includes`, nunca de otra área.
- Las rutas solicitan permisos, nunca roles. Los roles del sistema usan sus constantes estables.
- El seed otorga permisos a Admin; cambiar permisos de un rol conserva su revisión y no tiene caché que invalidar ([ADR 0032](../../../docs/decisions/0032-sin-redis-la-app-sigue.md)).

## Referencias

- [Checklist de un permiso nuevo](../../../docs/guides/permiso-nuevo.md).
- [Reglas de roles y permisos](../../../docs/features/administracion.md).
- [Pruebas del catálogo](../../../tests/ArquitecturaBase.Domain.UnitTests/Authorization).
