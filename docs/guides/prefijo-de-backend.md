# Prefijo de backend

Cómo sumar un prefijo de ruta del backend que no empieza con `/api` (`/metrics`, `/hooks`, lo que sea). Una ruta bajo un prefijo que ya existe no toca nada de esto: un controller nuevo en `api/<recurso>` está cubierto. La regla está en [`AGENTS.md`, "Front"](../../AGENTS.md#front); el porqué del origen único y del fallback del SPA, en [backend.md, "Front y hosting del SPA"](../architecture/backend.md#front-y-hosting-del-spa).

## Por qué hace falta

El navegador habla con un solo origen. En producción la Api sirve también el SPA, y su fallback (`UseSpaFallback`) contesta el `index.html` a todo GET o HEAD que no matcheó un endpoint, salvo que empiece con un prefijo de backend. En desarrollo, Vite sirve el SPA y reenvía a la Api solo los prefijos que tiene en su proxy. Las dos listas son a mano: un prefijo que falta en alguna hace que sus rutas inexistentes respondan **el `index.html` con 200**, y el cliente recibe HTML donde esperaba JSON.

## Los pasos

1. **`BackendPrefixes`**, en [`src/ArquitecturaBase.Api/Hosting/SpaExtensions.cs`](../../src/ArquitecturaBase.Api/Hosting/SpaExtensions.cs): sumá `"/<prefijo>"` al arreglo. Es lo que excluye el prefijo del fallback del SPA en producción.
2. **`SpaHostingTests`**, en [`tests/ArquitecturaBase.Api.IntegrationTests/Hosting/SpaHostingTests.cs`](../../tests/ArquitecturaBase.Api.IntegrationTests/Hosting/SpaHostingTests.cs): un `[InlineData("/<prefijo>/no-existe")]` en `Backend_routes_keep_returning_a_problem`, que pide la ruta inexistente y espera un ProblemDetails en lugar del `index.html`.
3. **El proxy de Vite**, **en el repo del front** (`../ArquitecturaBaseFront`): el prefijo en el `server.proxy` de `vite.config.ts`, con las mismas opciones que los que ya están (`changeOrigin: false`, por el issuer), para que en desarrollo Vite lo reenvíe a la Api. Es otro repo: va en su propio commit.
4. **El inventario de rutas, si son de negocio**: en [`ExplicitRouteInventoryTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Contracts/ExplicitRouteInventoryTests.cs), el prefijo en el filtro `.Where(route => route.Contains(" /account/", …) || …)` de `The_explicit_business_routes_have_no_missing_or_duplicate_method_path_pairs`, y cada ruta en `ExpectedRoutes`, con el total del `Assert.Equal(<número>, ExpectedRoutes.Length)` ([paso 13 de la receta](agregar-un-area.md#13-inventario-de-rutas)). Un prefijo sin rutas de negocio (como `/health` u `/openapi`) no entra en el inventario.

## Trampas

- **Sin el paso 1**, el problema se ve recién en producción: en desarrollo no hay `wwwroot/index.html` y el fallback ni se instala.
- **Sin el paso 3**, se ve solo en desarrollo: Vite contesta su propio `index.html` y la Api no recibe el pedido. Los tests del backend no lo ven.
- **Sin el filtro del paso 4**, las rutas del prefijo no entran en la comparación y el inventario pasa sin verlas.
- **El prefijo es un segmento entero:** el fallback compara con `StartsWithSegments`, así que `/metrics` cubre `/metrics/x` pero no `/metricsx`.
- **`SpaHostingTests` no recorre todos los prefijos de `BackendPrefixes`** (hoy prueba `/api`, `/account`, `/connect`, `/health` y `/webhooks`): que un prefijo esté en la lista no quiere decir que tenga su caso. El nuevo lo lleva.

## Lo verifica

Los dos tests son de integración y corren **solo con Docker**:

- `SpaHostingTests.Backend_routes_keep_returning_a_problem` (paso 2);
- `ExplicitRouteInventoryTests.The_explicit_business_routes_have_no_missing_or_duplicate_method_path_pairs` (paso 4).

El proxy de Vite no lo verifica ningún test: se prueba a mano con `aspire run`, pidiendo una ruta inexistente del prefijo en `https://localhost:5173` y mirando que responda un ProblemDetails.
