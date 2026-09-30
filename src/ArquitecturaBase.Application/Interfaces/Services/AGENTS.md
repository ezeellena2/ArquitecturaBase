# Interfaces de servicios: entrada a casos de uso

Define las operaciones que puede ejecutar el borde HTTP. Cada interfaz corresponde a un servicio que coordina el área.

## Al modificar

- Los métodos expresan casos de uso y usan modelos de Application; los fallos de negocio se devuelven con `Result`.
- Una operación que escribe tiene su límite en el servicio implementador, nunca en el controller ni en esta interfaz.
- No exponer entidades seguidas ni detalles de repositorios, EF, sesión HTTP o proveedores.

## Referencias

- [Recorrido de un caso de uso](../../../../docs/architecture/backend.md).
- [Interfaz de referencia](IRoleService.cs).
- [Implementación de referencia](../../Services/Roles/RoleService.cs).
