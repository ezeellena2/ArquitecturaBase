# Api.IntegrationTests: HTTP y proveedores reales

`ApiFactory` hospeda la Api con PostgreSQL y Redis de Testcontainers. Las carpetas agrupan contratos, áreas, persistencia, caché y módulos; `Support` comparte el arnés y `TestFeatures` contiene piezas exclusivas de prueba.

## Al modificar

- Reutilizá `ApiFactory` y sus scopes; no abrir transacciones desde un test si la acción que invocás ya abre su límite.
- Cada ruta agrega inventario, permisos, cuerpo, status, errores y OpenAPI. Probá también caminos fallidos y efectos persistidos.
- Usá identificadores o búsquedas propias por escenario y restaurá cambios compartidos para evitar interferencias.
- Para carreras usá coordinación explícita y hosts independientes cuando sea la garantía que querés verificar.
- Docker es obligatorio: ninguna sustitución en memoria demuestra restricciones PostgreSQL ni coherencia Redis.

## Referencias

- [Arnés de integración](../../docs/architecture/backend.md).
- [Factory compartida](Support/ApiFactory.cs).
- [Inventario de rutas](Contracts/ExplicitRouteInventoryTests.cs).
