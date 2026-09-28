# Permiso nuevo

Cómo sumar un permiso, para un área nueva o para una que ya existe, y cómo pedirlo desde una ruta. Las reglas que esta guía aplica están en [`AGENTS.md`, "Casos de uso MVC y borde HTTP"](../../AGENTS.md#casos-de-uso-mvc-y-borde-http); ante una diferencia, mandan ellas. Si el permiso es parte de un área nueva, seguí además el [paso 5 de la receta](agregar-un-area.md#5-permisos), que dice cómo se llaman sus dos permisos y dónde van en el catálogo.

En los nombres, `<área>` es el prefijo del permiso (`products`) y `<Área>` la clase que lo agrupa (`Products`). Las rutas de archivo son desde la raíz del repo.

## Los pasos

1. **El código, en Domain.** En [`src/ArquitecturaBase.Domain/Authorization/Permissions.cs`](../../src/ArquitecturaBase.Domain/Authorization/Permissions.cs), una constante adentro de la clase de su área (`public const string Manage = "<área>.manage";`, como `Permissions.Settings.Manage`), o una clase nueva `public static class <Área>` si el área no existe. La constante se suma también a `Permissions.All`, que es el catálogo: sin eso, el seed no se la da a nadie y la ruta que la pide no la reconoce.
   - El orden de `All` es el que ve el front: `RoleService.GetPermissionsAsync` agrupa por área en el orden en que aparecen. Un permiso de un área que existe va junto a los de su área; un área nueva, al final.
2. **Los textos, en los dos idiomas.** En [`src/ArquitecturaBase.Application/Resources/Permissions.resx`](../../src/ArquitecturaBase.Application/Resources/Permissions.resx) (español rioplatense, con voseo) y en `Permissions.en.resx`:
   - `Permission.<código>`: el nombre corto (`Ver roles`);
   - `PermissionDescription.<código>`: qué habilita (`Los roles y qué permisos da cada uno.`);
   - `Area.<área>`, solo si el área es nueva: el nombre del grupo (`Roles`).
3. **El seed: no se toca.** [`RoleSeeder`](../../src/ArquitecturaBase.Infrastructure/Persistence/Seed/RoleSeeder.cs) le da `Permissions.All` a Admin en cada arranque y suma los que falten, así que un permiso nuevo le llega a Admin al reiniciar la Api. El rol `User` no recibe ninguno.
4. **La ruta lo pide con `[HasPermission]`.** En el controller, `[HasPermission(Permissions.<Área>.<Acción>)]` en la acción o en la clase ([`HasPermissionAttribute.cs`](../../src/ArquitecturaBase.Api/Authorization/HasPermissionAttribute.cs)). Sin sesión responde 401; con sesión y sin el permiso en ninguno de sus roles, 403. Nunca `[Authorize(Policy = …)]` armado a mano ni `[Authorize(Roles = …)]`: las rutas piden permisos, no roles.
5. **Las listas fijas del catálogo.** Algunos tests fijan el catálogo a mano, a propósito, para que un cambio en él sea visible:
   - [`PermissionsTests.All_lists_every_permission_once`](../../tests/ArquitecturaBase.Domain.UnitTests/Authorization/PermissionsTests.cs) (Domain): todos los permisos, en el orden de `All`. Se toca siempre;
   - si el área es nueva, [`RoleServiceTests.Get_permissions_keeps_catalog_order_and_request_culture`](../../tests/ArquitecturaBase.Application.UnitTests/Services/Roles/RoleServiceTests.cs) (Application: las áreas, en orden) y, en [`RolesEndpointsTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Roles/RolesEndpointsTests.cs) (integración), `The_permission_catalog_is_grouped_by_area_and_translated` (las áreas y sus nombres en español) y `The_permission_catalog_is_also_in_english` (en inglés);
   - [`docs/features/administracion.md`](../features/administracion.md), "Reglas": en la frase "El catálogo queda en …" se suma el código nuevo, en el orden de `All`.

## `InvalidateRoleAsync`: cuándo sí y cuándo no

Los permisos de cada rol se cachean una hora (`PermissionService`, con `HybridCache`). Por eso, **un caso de uso que cambia los permisos de un rol** llama a `IPermissionService.InvalidateRoleAsync(roleId, ct)` después del commit y solo si se confirmó, como `RoleService.UpdateAsync` y `RoleService.DeleteAsync` ([`RoleService.cs`](../../src/ArquitecturaBase.Application/Services/Roles/RoleService.cs)). Invalidar antes del commit deja que una lectura concurrente vuelva a cachear los permisos viejos.

**Sumar un permiso al catálogo no lo pide:** el seed se lo agrega a Admin al arrancar, con el caché vacío.

## Trampas

- **El formato es `^[a-z]+\.[a-z]+$`:** área y acción en minúsculas, sin guiones, números ni otro punto (`product-lines.read` no pasa). Lo verifica `PermissionsTests.Permissions_follow_the_area_action_format`.
- **Una clave de texto que falta no rompe nada en ejecución:** `PermissionTexts` devuelve la clave misma (`Area.products`) y eso es lo que ve la pantalla de roles. `PermissionTextsTests` lo avisa, y solo si el permiso está en `All`.
- **Un permiso que no está en `All`** (un literal mal escrito en `[HasPermission]`, o una constante que no se sumó al catálogo) no se lo da el seed a nadie, así que la ruta queda cerrada para todos, Admin incluido. `PermissionAuthorizationTests.Every_required_permission_exists` lo avisa sin Docker.
- **Las listas fijas del paso 5 son a mano.** `RolesEndpointsTests` es de integración: sin Docker no avisa.

## Lo verifica

| Test | Proyecto | Qué |
|---|---|---|
| [`PermissionsTests`](../../tests/ArquitecturaBase.Domain.UnitTests/Authorization/PermissionsTests.cs) | Domain | `All` completo, en orden y sin repetidos; el formato |
| [`PermissionTextsTests`](../../tests/ArquitecturaBase.Application.UnitTests/Resources/PermissionTextsTests.cs) | Application | cada `Area.*`, `Permission.*` y `PermissionDescription.*`, en español y en inglés |
| [`ResourceParityTests`](../../tests/ArquitecturaBase.Application.UnitTests/Resources/ResourceParityTests.cs) | Application | `Permission_texts_have_the_same_keys_in_spanish_and_english` |
| [`PermissionAuthorizationTests`](../../tests/ArquitecturaBase.ArchitectureTests/PermissionAuthorizationTests.cs) | Architecture | `[HasPermission]` en lugar de la política a mano, y que cada permiso pedido exista |
| `RolesEndpointsTests`, `SeedTests`, `UsersEndpointsTests.Admin_gets_every_permission` | IntegrationTests (Docker) | el catálogo por HTTP y que Admin tenga `Permissions.All` |
