# 0004. Roles como área de referencia

**Estado:** Aceptada, 2026-09-26.

**Origen:** decisión D4 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), que sale del code review del 2026-09-26. Se implementa en la Etapa 4.

## Contexto

El code review encontró que no hay una receta ni un área de referencia. Para agregar un área nueva, una persona o una IA tiene que deducir qué piezas copiar mirando áreas que hoy mezclan estilos, y Usuarios, la más completa, está entrelazada con Auth y WhatsApp.

## Decisión

**Roles es el área de referencia**, completada con paginado, obtener por id y metadatos OpenAPI.

## Consecuencias

- Roles tiene que mostrar todas las piezas de un área:
  - la entidad (vía Identity) y su configuración de EF;
  - `RoleErrors` y sus claves en los dos `.resx`;
  - los permisos;
  - su repositorio y su lector, con sus implementaciones;
  - `IRoleService` y `RoleService`;
  - los modelos y sus validadores;
  - `RolesController` y sus contratos;
  - tests en los tres niveles: unitario del servicio, integración de las rutas y del lector.
- La receta para agregar un área (Etapa 4) enlaza cada paso al archivo equivalente de Roles.
- Roles tiene que mostrar los idiomas vigentes de la plantilla, no los viejos. Por eso se completa después de la Etapa 3.
- Completarla cambia `GET /api/roles`, que pasa a paginado. Antes hay que revisar el front: si no pagina, la forma de la respuesta se mantiene y el paginado entra como query opcional.
- La entidad de Roles viene de Identity. La receta tiene que explicar aparte cómo es una entidad propia de Domain.

**Enmienda (2026-09-28, decisión del usuario en la Etapa 4):** el paginado no entra como query opcional de `GET /api/roles`, sino en una ruta aparte, `GET /api/roles/paged`. `GET /api/roles` queda como está, el catálogo completo que usan los selectores del front. Con una query opcional, la misma operación respondería una lista o un `PagedResult<RoleResponse>` según el pedido: OpenAPI no puede declarar dos esquemas de éxito para una operación y `OpenApiTests` exige que cada una declare su 2xx con esquema. Un área nueva no tiene ese problema y pone su listado paginado directamente en `GET /api/<recurso>`; `/paged` existe solo porque el `GET` de Roles ya era el catálogo.

## Alternativas descartadas

- **Un área de ejemplo creada solo para mostrar el patrón.** Un proyecto derivado la borraría, y con ella se iría la referencia. Roles está en todo proyecto derivado, así que la referencia nunca se borra.
- **Usuarios.** Es la más completa, pero también la más grande, y está entrelazada con Auth y WhatsApp. Roles es chica y ya sigue el camino canónico.
