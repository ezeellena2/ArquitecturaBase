# Interfaces: puertos de la aplicación

Las dependencias se definen por su función: `Services`, entrada a casos de uso; `Persistence`, acceso a datos; `Integrations`, proveedores técnicos; `Channels`, extensión por canales opcionales.

## Al modificar

- Un controller consume `Services`; un servicio puede consumir persistencia y proveedores. No devolver `IQueryable` ni tipos técnicos a través de un puerto.
- Agregá contratos cuando exista un consumidor real; no generar uno por entidad ni introducir repositorios genéricos.
- Los puertos del núcleo no nombran implementaciones de módulos. Los contratos propios de un módulo viven en su `Modules/<Módulo>/Interfaces`.

## Referencias

- [Dependencias y módulos opcionales](../../../docs/architecture/backend.md).
- [Ubicación de contratos](../../../docs/guides/agregar-un-area.md).
