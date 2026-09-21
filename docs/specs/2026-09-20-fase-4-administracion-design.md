# Fase 4 (Administración) — Diseño

Sigue el spec maestro `docs/specs/2026-09-18-arquitectura-base-design.md`, sección 10. Lo que no se diga acá, vale de ahí.

## 1. Objetivo

Que un administrador pueda manejar el sistema **sin tocar la base de datos ni la configuración del servidor**: dar de alta usuarios, asignarles roles, crear roles con sus permisos y decidir si el sistema está abierto o es solo por invitación.

Terminada cuando un administrador, desde el navegador y sin ayuda de nadie, puede: dar de alta a otra persona, darle un rol que él mismo creó, y cambiar el modo de registro del sistema.

## 2. Alcance

**Entra:** ABM de usuarios, ABM de roles con sus permisos, configuración del sistema y edición del perfil propio.

**No entra:**
- **Dispositivos y sesiones** (ver las sesiones activas y cerrarlas a distancia). El spec maestro las pone en la misma fase, pero son otro subsistema: se administran autorizaciones y tokens de OpenIddict, no Identity. Van a una Fase 5 con su propio spec.
- **Multi-empresa.** Se evaluó y se descartó el 2026-09-20: cada producto construido con esta base es su propio despliegue, con su propia base de datos, y el `Admin` de esa instalación es el dueño. No hay un superadministrador por encima de las cuentas. Si alguna vez varias empresas tienen que convivir en una sola instalación, es una decisión arquitectónica que atraviesa todo el modelo de datos y merece su propia fase.

## 3. Decisiones (2026-09-20)

- **Los permisos vienen solo de los roles.** No hay permisos sueltos por usuario: quien necesite una combinación distinta, crea un rol. Es lo que ya resuelve `PermissionService` con su caché, y evita dos fuentes de verdad.
- **Invitar es dar de alta el correo.** Como el ingreso es sin contraseña, no hacen falta correos de invitación con enlaces que vencen: el administrador da de alta la dirección y la persona entra por el camino de siempre, con su código.
- **El modo de registro se cambia desde el panel**, no con una variable de entorno. Desplegar un producto nuevo tiene que ser levantarlo y configurarlo desde adentro.
- **Los roles se crean y se editan.** `Admin` y `User` quedan protegidos.

## 4. Quién puede entrar: el modo de registro

Un ajuste del sistema, `RegistrationMode`, con dos valores:

| Modo | Qué pasa con un correo que no tiene cuenta |
|---|---|
| `InviteOnly` | No entra. La cuenta la tiene que crear un administrador. |
| `Open` | Se crea la cuenta sola, con el rol `User`. Es lo de hoy. |

El valor inicial, al crear la base, sale de `Registration:Mode` en la configuración, y por defecto es **`InviteOnly`**: una plantilla se despliega cerrada y se abre a propósito.

**Dónde se aplica, y con qué cuidado:**

- **Código por correo.** `POST /account/login-code` **sigue respondiendo siempre `202`**, y en `InviteOnly`, si el correo no tiene cuenta, **genera el código pero no encola ningún email**. Quien pruebe una dirección ajena recibe el mismo `202` de siempre y ningún correo; si después intenta verificar, falla como cualquier código que no le llegó.

  El código se genera aunque no se mande, y eso es a propósito: los límites por dirección (el de reenvío y el de intentos) se apoyan en esa fila, así que saltearla haría que una dirección registrada empiece a responder `429` al insistir mientras una desconocida responde `202` para siempre. Esa diferencia alcanza para enumerar qué correos tienen cuenta, que es exactamente lo que este modo tiene que impedir. Las filas que nadie usa vencen solas a los 10 minutos.
- **Google.** Acá la persona ya probó ser dueña de la dirección, así que no hay nada que proteger: si el correo no tiene cuenta y el modo es `InviteOnly`, vuelve al ingreso con un mensaje claro (`Account.NotInvited`), que le dice que pida acceso a un administrador.
- **Cambiar de `Open` a `InviteOnly` no expulsa a nadie.** El modo decide quién puede *crear* una cuenta. Para sacar a alguien que ya entró, se lo desactiva.

## 5. Configuración del sistema

Una entidad `SystemSettings` de **una sola fila**, en Domain, auditable (`IAuditable`): un ajuste que decide quién puede entrar al sistema tiene que dejar rastro de quién lo cambió y cuándo, y eso sale gratis con los interceptores que ya existen.

- Primer y único ajuste por ahora: `RegistrationMode`. Cada ajuste nuevo es una propiedad con su tipo y su migración, no un par clave-valor sin forma: así se valida de verdad y no se guardan strings sueltos.
- Se lee cacheada con `HybridCache`, igual que los permisos, y el caché se invalida al guardar: el cambio vale al instante, sin reiniciar nada.
- La fila la crea el seed con el valor de configuración. **Si la fila ya existe, manda la base**: un despliegue nunca pisa lo que se configuró desde el panel.

## 6. Roles y permisos

- **Crear** un rol: nombre único y descripción. `ApplicationRole` ya tiene `Description`; se le suma `IAuditable`.
- **Editar**: nombre, descripción y qué permisos tiene, que se guardan como role claims de tipo `permission`, como hoy.
- **Borrar**: solo si no es del sistema y **no tiene usuarios asignados**. Si los tiene, el error dice cuántos son, para que se reasignen primero.
- **`Admin` y `User` son del sistema** (`SystemRoles`): no se borran ni se renombran. `Admin` conserva siempre todos los permisos y no se le editan: es la garantía de que existe alguien que puede arreglar cualquier cosa.
- Todo cambio en los permisos de un rol llama a `IPermissionService.InvalidateRoleAsync`.
- Se suma un permiso al catálogo: **`settings.manage`**, que el seed le da a `Admin`. El catálogo queda en `users.read`, `users.manage`, `roles.read`, `roles.manage`, `settings.manage`.

## 7. Usuarios

- **Alta:** correo (obligatorio), nombre y roles (opcionales). Si el correo ya tiene cuenta activa, `Users.AlreadyExists`. Si tiene una cuenta **borrada lógicamente, se restaura** en vez de fallar: el correo es único y que la dirección quede inutilizable para siempre sorprende a cualquiera. La cuenta restaurada queda con los roles que diga el alta, no con los que tenía antes: devolverle permisos viejos sin que nadie lo pida es la clase de sorpresa que no se quiere en un panel de administración.
- **Editar:** nombre y roles.
- **Activar y desactivar:** el campo `IsActive` ya existe y ya se respeta al ingresar.
- **Eliminar:** borrado lógico. **`ApplicationUser` todavía no implementa `ISoftDeletable`**: esta fase se lo agrega, con su migración y su filtro global, para que un usuario borrado desaparezca de los listados y no pueda entrar, pero su historial de ingresos siga existiendo.

**Desactivar o eliminar tiene que cortar el acceso en el momento.** Hoy no alcanza con marcar la fila: quien ya entró tiene un access token que vale 15 minutos y una cookie que vale 30 días, así que seguiría trabajando como si nada. Las dos acciones tienen que, además:

- **revocar las autorizaciones y los tokens** de esa persona en OpenIddict, que con `EnableTokenEntryValidation` ya activo deja sus access y refresh tokens sin valor de inmediato;
- **invalidar su cookie de sesión**, actualizando su `SecurityStamp` con Identity.

Sin esto, "desactivar" es una etiqueta en la base que no impide nada, y es justo la acción que se usa cuando alguien se va de la empresa.

## 8. Las reglas que impiden romper el sistema

Un panel de administración mal hecho deja al dueño afuera de su propio sistema. Estas reglas viven en Domain, con sus tests unitarios:

- Nadie se puede **sacar a sí mismo el rol `Admin`**.
- Nadie puede **desactivar ni eliminar su propia cuenta**.
- Siempre tiene que quedar **al menos un usuario activo con rol `Admin`**. Vale para quitar el rol, desactivar y eliminar.
- No se borra un rol que tenga usuarios asignados.

## 9. Perfil propio

`PUT /api/me` deja cambiar `displayName`, `culture` y `timeZoneId`. Con eso, **el idioma deja de vivir solo en el navegador** y pasa a la cuenta, que es el pendiente que dejó la Fase 3.

`GET /api/me` suma `lastLoginAtUtc`, que sale de `LoginAudits` y completa la tarjeta del tablero.

## 10. Endpoints

| Método | Ruta | Permiso | Qué hace |
|---|---|---|---|
| POST | `/api/users` | `users.manage` | da de alta un correo |
| GET | `/api/users/{id}` | `users.read` | detalle con sus roles |
| PUT | `/api/users/{id}` | `users.manage` | nombre y roles |
| POST | `/api/users/{id}/activate` | `users.manage` | reactiva |
| POST | `/api/users/{id}/deactivate` | `users.manage` | desactiva |
| DELETE | `/api/users/{id}` | `users.manage` | borrado lógico |
| GET | `/api/roles` | `roles.read` | listado con cantidad de usuarios |
| POST | `/api/roles` | `roles.manage` | crea |
| PUT | `/api/roles/{id}` | `roles.manage` | nombre, descripción y permisos |
| DELETE | `/api/roles/{id}` | `roles.manage` | borra |
| GET | `/api/permissions` | `roles.read` | catálogo, agrupado por el prefijo del código (`users`, `roles`, `settings`), con el nombre de cada área y de cada permiso traducidos |
| GET | `/api/settings` | `settings.manage` | ajustes del sistema |
| PUT | `/api/settings` | `settings.manage` | cambia el modo de registro |
| PUT | `/api/me` | bearer | perfil propio |

Cada uno es un caso de uso con su handler y su validador, como el resto.

## 11. Pantallas

- **`/usuarios`**: sobre el listado que ya existe, un botón "Nuevo usuario" y acciones por fila (cambiar roles, activar o desactivar, eliminar). Todo en diálogos: no hace falta una pantalla de detalle.
- **`/roles`**: listado con nombre, descripción y cuántos usuarios tiene cada rol. El alta y la edición, en un diálogo con el nombre y los permisos como casillas agrupadas por área. Entra al menú bajo *Administración*, donde la Fase 3 la dejó anotada y oculta.
- **`/configuracion`**: el interruptor del modo de registro, con una línea que explique qué implica cada opción. Solo la ve quien tenga `settings.manage`.
- Las acciones destructivas (eliminar, desactivar, borrar un rol) van con el diálogo de confirmación que ya existe, diciendo qué se pierde.

## 12. Errores

Códigos estables, con el formato de siempre y su traducción en los dos idiomas:

`Users.AlreadyExists`, `Users.NotFound`, `Users.CannotModifySelf`, `Users.LastAdmin`, `Roles.NotFound`, `Roles.AlreadyExists`, `Roles.SystemRoleCannotChange`, `Roles.HasUsers`, `Account.NotInvited`.

## 13. Tests

- **Unitarios (Domain y Application):** las cuatro reglas de la sección 8, una por una, incluido el caso de borde de que el último administrador activo no se pueda ir de ninguna de las tres formas.
- **Integración:** cada endpoint, con el permiso y sin él (403 con ProblemDetails). Los dos modos de registro: que en `InviteOnly` un correo desconocido reciba `202` y **ningún correo**, y que con Google vuelva con `Account.NotInvited`. Que cambiar el modo desde el panel tenga efecto sin reiniciar. Y el que más importa: que **después de desactivar a alguien, su access token y su cookie dejen de servir en el acto** — un test que entra, desactiva, y comprueba que la misma sesión ya no pasa.
- **Front:** las tres pantallas con Vitest y MSW, incluido que el menú y las acciones se filtren por permiso.

## 14. Pendientes que esta fase no toca

- Dispositivos y sesiones: Fase 5.
- El hueco de despliegue de la Fase 3: nada copia el front compilado al `wwwroot` de la Api.
- `UseForwardedHeaders`, que hace falta para desplegar detrás de un proxy.
