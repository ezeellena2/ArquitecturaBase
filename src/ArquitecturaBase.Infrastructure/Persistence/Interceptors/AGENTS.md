# Interceptors: auditoría y borrado lógico

Completa campos auditables al guardar y convierte el borrado lógico en modificación. Es la única fuente de esos campos para entidades con las interfaces del dominio.

## Al modificar

- El registro ejecuta soft delete antes de auditoría; preservá ese orden.
- Las fechas vienen de `TimeProvider` y la identidad del puerto de usuario actual. No usar el reloj del sistema ni asignar estos campos en servicios.
- Las operaciones bulk de EF saltean interceptores; no usarlas con estas entidades.
- Verificá inserción, modificación, borrado y ocultamiento de filas con los tests de persistencia.

## Referencias

- [Persistencia y auditoría](../../../../docs/architecture/backend.md).
- [Pruebas de interceptores y filtros](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Persistence).
