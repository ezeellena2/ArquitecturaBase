# Authorization: catálogo de permisos

Define permisos, roles del sistema y errores de roles. El catálogo es compartido por servicios, seed, autorización HTTP y traducciones.

## Al modificar

- Agregá un permiso a su grupo y a `Permissions.All`; completá sus textos en ambos recursos de Application.
- Las rutas solicitan permisos, nunca roles. Los roles del sistema usan sus constantes estables.
- El seed otorga permisos a Admin; cambiar permisos de un rol requiere invalidación después del commit y conservar su revisión.

## Referencias

- [Checklist de un permiso nuevo](../../../docs/guides/permiso-nuevo.md).
- [Reglas de roles y permisos](../../../docs/features/administracion.md).
- [Pruebas del catálogo](../../../tests/ArquitecturaBase.Domain.UnitTests/Authorization).
