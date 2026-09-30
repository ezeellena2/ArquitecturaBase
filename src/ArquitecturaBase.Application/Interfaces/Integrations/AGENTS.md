# Puertos de proveedores técnicos

Agrupa contratos de Identity, seguridad, correo, petición, teléfonos y descarte de caché. Sus implementaciones viven en Api o Infrastructure según la responsabilidad.

## Al modificar

- Los puertos expresan lo que necesita un caso de uso; no exponer SDKs, Redis, `HttpContext` ni tipos de Identity.
- `Request` se implementa en Api/RequestContext; los demás adaptadores técnicos van en Infrastructure.
- Un caché se consume por el puerto de su área; el descarte se coordina después del commit. Los permisos se invalidan por `IPermissionService`.
- Los contratos de un canal opcional que debe desconocer el núcleo pertenecen a `Interfaces/Channels`.

## Referencias

- [Puertos y límites entre capas](../../../../docs/architecture/backend.md).
- [Cómo agregar caché](../../../../docs/guides/agregar-cache.md).
- [Adaptadores de la petición](../../../ArquitecturaBase.Api/RequestContext/AGENTS.md).
