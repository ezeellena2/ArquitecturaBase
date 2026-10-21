# Authorization: permisos por petición

Los atributos, el proveedor de políticas y el handler conectan `HasPermission` y `HasAnyPermission` con el servicio de permisos. El catálogo funcional está en Domain.

## Al modificar

- Las acciones usan `[HasPermission(Permissions.X)]`. Cuando alcanza cualquiera de varios permisos, usan `[HasAnyPermission(Permissions.X, Permissions.Y)]`; cada alternativa pertenece al catálogo. Varios atributos se cumplen todos (AND); las alternativas de uno se cumplen con cualquiera (OR). No introducir políticas manuales ni autorización por roles.
- Sin sesión se responde 401; con sesión sin permiso, 403. El pipeline completa sus ProblemDetails.
- El handler consume `IPermissionService`: no resuelve claims de roles como permisos ni consulta EF directamente.
- Un permiso nuevo cambia catálogo, traducciones, seed y tests de acceso. La caché del rol vive en Infrastructure.

## Referencias

- [Agregar y verificar permisos](../../../docs/guides/permiso-nuevo.md).
- [Autorización funcional](../../../docs/features/administracion.md).
- [Pruebas 401 y 403](../../../tests/ArquitecturaBase.Api.IntegrationTests/Contracts).
